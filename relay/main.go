// LapCont — Relay — Role/route-bound opaque WebSocket forwarding, no offline command queue
// License: MIT
package main

import (
	"context"
	"crypto/sha256"
	"crypto/subtle"
	"crypto/tls"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"github.com/coder/websocket"
	"io"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"
)

type credential struct {
	Hash  string `json:"sha256"`
	Route string `json:"route"`
	Phone string `json:"phone"`
	Role  string `json:"role"`
}
type options struct {
	MaxPairs             int
	MaxBytesPerSecond    int
	MaxMessagesPerSecond int
	Idle                 time.Duration
}
type endpoint struct {
	conn   *websocket.Conn
	cancel context.CancelFunc
	joined time.Time
}
type pair struct{ agent, phone *endpoint }
type relay struct {
	mu             sync.Mutex
	credentials    []credential
	revoked        map[string]bool
	pairs          map[string]*pair
	options        options
	revocationFile string
}

func newRelay(credentials []credential, opts options) (*relay, error) {
	if opts.MaxPairs < 1 || opts.MaxPairs > 4096 || opts.MaxBytesPerSecond < 65536 || opts.MaxMessagesPerSecond < 1 || opts.Idle < time.Second {
		return nil, errors.New("invalid limits")
	}
	if len(credentials) == 0 || len(credentials) > 8192 {
		return nil, errors.New("invalid credential count")
	}
	for _, c := range credentials {
		b, e := hex.DecodeString(c.Hash)
		if e != nil || len(b) != 32 || !id(c.Route) || !id(c.Phone) || c.Role != "agent" && c.Role != "phone" {
			return nil, errors.New("invalid route credential")
		}
	}
	return &relay{credentials: credentials, revoked: map[string]bool{}, pairs: map[string]*pair{}, options: opts}, nil
}

// loadRevocations fails startup on malformed or missing configured state; revocation survives restart.
func (r *relay) loadRevocations(path string) error {
	r.revocationFile = path
	data, err := os.ReadFile(path)
	if os.IsNotExist(err) {
		return r.saveRevocations()
	}
	if err != nil || len(data) > 1024*1024 {
		return errors.New("revocation state unavailable")
	}
	var entries []string
	if json.Unmarshal(data, &entries) != nil || len(entries) > 8192 {
		return errors.New("invalid revocation state")
	}
	for _, entry := range entries {
		fields := strings.Split(entry, ":")
		if len(fields) != 2 || !id(fields[0]) || !id(fields[1]) {
			return errors.New("invalid revocation route")
		}
		r.revoked[entry] = true
	}
	return nil
}

// Caller holds mu after startup. Only route IDs are persisted, never tokens or opaque carrier bytes.
func (r *relay) saveRevocations() error {
	if r.revocationFile == "" {
		return nil
	}
	entries := make([]string, 0, len(r.revoked))
	for entry := range r.revoked {
		entries = append(entries, entry)
	}
	data, err := json.Marshal(entries)
	if err != nil {
		return err
	}
	if err = os.WriteFile(r.revocationFile+".new", data, 0600); err != nil {
		return err
	}
	return os.Rename(r.revocationFile+".new", r.revocationFile)
}
func id(s string) bool { _, err := hex.DecodeString(s); return len(s) == 32 && err == nil }
func (r *relay) authenticate(req *http.Request) (credential, bool) {
	auth := req.Header.Get("Authorization")
	if !strings.HasPrefix(auth, "Bearer ") || len(auth) < 39 || len(auth) > 519 || req.URL.RawQuery != "" {
		return credential{}, false
	}
	sum := sha256.Sum256([]byte(strings.TrimPrefix(auth, "Bearer ")))
	route, phone, role := req.Header.Get("X-LapCont-Route"), req.Header.Get("X-LapCont-Phone"), req.Header.Get("X-LapCont-Role")
	for _, c := range r.credentials {
		expected, _ := hex.DecodeString(c.Hash)
		if subtle.ConstantTimeCompare(sum[:], expected) == 1 && c.Route == route && c.Phone == phone && c.Role == role {
			r.mu.Lock()
			revoked := r.revoked[route+":"+phone]
			r.mu.Unlock()
			return c, !revoked
		}
	}
	return credential{}, false
}
func (r *relay) handler() http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("/health", func(w http.ResponseWriter, req *http.Request) {
		if req.Method != http.MethodGet {
			w.WriteHeader(405)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		io.WriteString(w, `{"status":"ok"}`)
	})
	mux.HandleFunc("/ws", r.ws)
	mux.HandleFunc("/revoke", r.revoke)
	return mux
}
func (r *relay) revoke(w http.ResponseWriter, req *http.Request) {
	c, ok := r.authenticate(req)
	if !ok || c.Role != "agent" || req.Method != http.MethodPost {
		w.WriteHeader(403)
		return
	}
	r.mu.Lock()
	r.revoked[c.Route+":"+c.Phone] = true
	persistError := r.saveRevocations()
	for k, p := range r.pairs {
		if strings.HasPrefix(k, c.Route+":"+c.Phone+":") {
			if p.agent != nil {
				p.agent.cancel()
				p.agent.conn.CloseNow()
			}
			if p.phone != nil {
				p.phone.cancel()
				p.phone.conn.CloseNow()
			}
			delete(r.pairs, k)
		}
	}
	r.mu.Unlock()
	if persistError != nil {
		w.WriteHeader(503)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}
func (r *relay) ws(w http.ResponseWriter, req *http.Request) {
	c, ok := r.authenticate(req)
	channel := req.Header.Get("X-LapCont-Channel")
	if !ok {
		w.WriteHeader(403)
		return
	}
	if channel != "control" && channel != "media" {
		w.WriteHeader(400)
		return
	}
	key := c.Route + ":" + c.Phone + ":" + channel
	// Arbitration occurs before upgrade; disconnected agent means unavailable, never queued commands.
	r.mu.Lock()
	if r.revoked[c.Route+":"+c.Phone] {
		r.mu.Unlock()
		w.WriteHeader(403)
		return
	}
	p := r.pairs[key]
	if p == nil {
		if c.Role != "agent" {
			r.mu.Unlock()
			w.WriteHeader(503)
			return
		}
		if len(r.pairs) >= r.options.MaxPairs {
			r.mu.Unlock()
			w.WriteHeader(429)
			return
		}
		p = &pair{}
		r.pairs[key] = p
	}
	if c.Role == "agent" && p.agent != nil || c.Role == "phone" && (p.phone != nil || p.agent == nil) {
		r.mu.Unlock()
		w.WriteHeader(409)
		return
	}
	conn, err := websocket.Accept(w, req, &websocket.AcceptOptions{CompressionMode: websocket.CompressionDisabled})
	if err != nil {
		if p.agent == nil && p.phone == nil {
			delete(r.pairs, key)
		}
		r.mu.Unlock()
		return
	}
	ctx, cancel := context.WithCancel(req.Context())
	e := &endpoint{conn: conn, cancel: cancel, joined: time.Now()}
	if c.Role == "agent" {
		p.agent = e
	} else {
		p.phone = e
	}
	r.mu.Unlock()
	defer func() {
		cancel()
		conn.CloseNow()
		r.mu.Lock()
		defer r.mu.Unlock()
		if r.pairs[key] != p {
			return
		}
		if p.agent != nil {
			p.agent.cancel()
			p.agent.conn.CloseNow()
		}
		if p.phone != nil {
			p.phone.cancel()
			p.phone.conn.CloseNow()
		}
		delete(r.pairs, key)
	}()
	conn.SetReadLimit(65536)
	heartbeatDone := make(chan struct{})
	go func() {
		defer close(heartbeatDone)
		tick := time.NewTicker(20 * time.Second)
		defer tick.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-tick.C:
				ping, stop := context.WithTimeout(ctx, 5*time.Second)
				err := conn.Ping(ping)
				stop()
				if err != nil {
					cancel()
					conn.CloseNow()
					return
				}
			}
		}
	}()
	defer func() { cancel(); <-heartbeatDone }()
	window := time.Now()
	bytes, messages := 0, 0
	for {
		deadline, stop := context.WithTimeout(ctx, r.options.Idle)
		typ, payload, err := conn.Read(deadline)
		stop()
		if err != nil {
			return
		}
		if typ != websocket.MessageBinary || len(payload) < 1 || len(payload) > 65536 {
			conn.Close(websocket.StatusPolicyViolation, "invalid carrier")
			return
		}
		if time.Since(window) >= time.Second {
			window = time.Now()
			bytes = 0
			messages = 0
		}
		bytes += len(payload)
		messages++
		if bytes > r.options.MaxBytesPerSecond || messages > r.options.MaxMessagesPerSecond {
			conn.Close(websocket.StatusPolicyViolation, "rate limit")
			return
		}
		r.mu.Lock()
		target := p.phone
		if c.Role == "phone" {
			target = p.agent
		}
		r.mu.Unlock()
		if target == nil {
			conn.Close(websocket.StatusTryAgainLater, "peer unavailable")
			return
		}
		write, done := context.WithTimeout(ctx, 3*time.Second)
		err = target.conn.Write(write, websocket.MessageBinary, payload)
		done()
		if err != nil {
			return
		}
	}
}
func main() {
	listen := flag.String("listen", ":8443", "TLS bind address")
	cert := flag.String("tls-cert", "", "mounted certificate chain")
	key := flag.String("tls-key", "", "mounted key")
	routes := flag.String("credentials", "", "protected route credential hash file")
	state := flag.String("revocations", "", "required writable protected revocation state file")
	max := flag.Int("max-pairs", 128, "bounded channel pairs")
	rate := flag.Int("byte-rate", 10*1024*1024, "bytes per second per endpoint")
	messages := flag.Int("message-rate", 1000, "binary chunks per second per endpoint")
	flag.Parse()
	if *cert == "" || *key == "" || *routes == "" || *state == "" {
		fmt.Fprintln(os.Stderr, "TLS certificate/key and protected credential hashes required")
		os.Exit(2)
	}
	data, err := os.ReadFile(*routes)
	if err != nil {
		slog.Error("credential file unavailable")
		os.Exit(2)
	}
	if len(data) > 1024*1024 {
		slog.Error("credential file too large")
		os.Exit(2)
	}
	var credentials []credential
	decoder := json.NewDecoder(strings.NewReader(string(data)))
	decoder.DisallowUnknownFields()
	if decoder.Decode(&credentials) != nil {
		slog.Error("invalid credential schema")
		os.Exit(2)
	}
	r, err := newRelay(credentials, options{*max, *rate, *messages, 90 * time.Second})
	if err != nil {
		slog.Error("invalid configuration")
		os.Exit(2)
	}
	if err := r.loadRevocations(*state); err != nil {
		slog.Error("revocation state unavailable")
		os.Exit(2)
	}
	server := &http.Server{Addr: *listen, Handler: r.handler(), ReadHeaderTimeout: 5 * time.Second, IdleTimeout: 90 * time.Second, MaxHeaderBytes: 8192, TLSConfig: &tls.Config{MinVersion: tls.VersionTLS12}}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		<-ctx.Done()
		r.mu.Lock()
		for _, p := range r.pairs {
			if p.agent != nil {
				p.agent.cancel()
				p.agent.conn.CloseNow()
			}
			if p.phone != nil {
				p.phone.cancel()
				p.phone.conn.CloseNow()
			}
		}
		r.mu.Unlock()
		timeout, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		server.Shutdown(timeout)
	}()
	slog.Info("relay started", "address", *listen)
	if err := server.ListenAndServeTLS(*cert, *key); err != nil && !errors.Is(err, http.ErrServerClosed) {
		slog.Error("relay stopped", "error_type", fmt.Sprintf("%T", err))
		os.Exit(1)
	}
}
