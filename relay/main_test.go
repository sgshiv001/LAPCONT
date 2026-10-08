// LapCont — Tests — Real TLS relay boundaries, route authorization and disconnected-peer policy
// License: MIT
package main

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"github.com/coder/websocket"
	"io"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

const pcID = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
const phoneID = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
const agentToken = "c6f99c75e9fc0a02f9fdcb803eff67e3f8bc342a8f9ee54a4dfc8878797b186af"
const phoneToken = "ef7e521d20dbd8e15b4c752b1397f2bb96c604a088f154018045c5fb70fcb50ee"

func fixture(t *testing.T, opts options) (*relay, *httptest.Server) {
	t.Helper()
	h1 := sha256.Sum256([]byte(agentToken))
	h2 := sha256.Sum256([]byte(phoneToken))
	r, e := newRelay([]credential{{hex.EncodeToString(h1[:]), pcID, phoneID, "agent"}, {hex.EncodeToString(h2[:]), pcID, phoneID, "phone"}}, opts)
	if e != nil {
		t.Fatal(e)
	}
	s := httptest.NewTLSServer(r.handler())
	t.Cleanup(s.Close)
	return r, s
}
func headers(role, channel string) http.Header {
	token := phoneToken
	if role == "agent" {
		token = agentToken
	}
	h := http.Header{}
	h.Set("Authorization", "Bearer "+token)
	h.Set("X-LapCont-Route", pcID)
	h.Set("X-LapCont-Phone", phoneID)
	h.Set("X-LapCont-Role", role)
	h.Set("X-LapCont-Channel", channel)
	return h
}
func dial(s *httptest.Server, h http.Header) (*websocket.Conn, *http.Response, error) {
	ctx, c := context.WithTimeout(context.Background(), 3*time.Second)
	defer c()
	return websocket.Dial(ctx, "wss"+strings.TrimPrefix(s.URL, "https")+"/ws", &websocket.DialOptions{HTTPClient: s.Client(), HTTPHeader: h})
}
func defaults() options { return options{16, 10 * 1024 * 1024, 1000, 5 * time.Second} }
func TestRevocationSurvivesRestart(t *testing.T) {
	r, s := fixture(t, defaults())
	path := filepath.Join(t.TempDir(), "revoked.json")
	if err := r.loadRevocations(path); err != nil {
		t.Fatal(err)
	}
	request, _ := http.NewRequest(http.MethodPost, s.URL+"/revoke", nil)
	request.Header = headers("agent", "control")
	response, err := s.Client().Do(request)
	if err != nil {
		t.Fatal(err)
	}
	response.Body.Close()
	if response.StatusCode != 204 {
		t.Fatal(response.StatusCode)
	}
	restarted, err := newRelay(r.credentials, defaults())
	if err != nil {
		t.Fatal(err)
	}
	if err = restarted.loadRevocations(path); err != nil {
		t.Fatal(err)
	}
	request.Header = headers("phone", "control")
	if _, ok := restarted.authenticate(request); ok {
		t.Fatal("revoked credential resurrected after restart")
	}
}
func TestHealthIsMinimal(t *testing.T) {
	_, s := fixture(t, defaults())
	r, e := s.Client().Get(s.URL + "/health")
	if e != nil {
		t.Fatal(e)
	}
	defer r.Body.Close()
	b, _ := io.ReadAll(r.Body)
	if string(b) != `{"status":"ok"}` {
		t.Fatal(string(b))
	}
}
func TestRoleRouteAndPhoneAreCredentialsNotIdentifiers(t *testing.T) {
	_, s := fixture(t, defaults())
	for _, field := range []string{"Authorization", "X-LapCont-Route", "X-LapCont-Phone", "X-LapCont-Role"} {
		h := headers("agent", "control")
		h.Set(field, "wrong")
		c, r, e := dial(s, h)
		if c != nil {
			c.CloseNow()
		}
		if e == nil || r == nil || r.StatusCode != 403 {
			t.Fatalf("%s not rejected", field)
		}
	}
}
func TestOfflinePhoneIsUnavailable(t *testing.T) {
	_, s := fixture(t, defaults())
	c, r, e := dial(s, headers("phone", "control"))
	if c != nil {
		c.CloseNow()
	}
	if e == nil || r.StatusCode != 503 {
		t.Fatal("offline command could be queued")
	}
}
func TestArbitrationAndBinaryExactForwarding(t *testing.T) {
	_, s := fixture(t, defaults())
	a, _, e := dial(s, headers("agent", "control"))
	if e != nil {
		t.Fatal(e)
	}
	defer a.CloseNow()
	duplicate, r, e := dial(s, headers("agent", "control"))
	if duplicate != nil {
		duplicate.CloseNow()
	}
	if e == nil || r.StatusCode != 409 {
		t.Fatal("duplicate agent allowed")
	}
	p, _, e := dial(s, headers("phone", "control"))
	if e != nil {
		t.Fatal(e)
	}
	defer p.CloseNow()
	d, r, e := dial(s, headers("phone", "control"))
	if d != nil {
		d.CloseNow()
	}
	if e == nil || r.StatusCode != 409 {
		t.Fatal("duplicate controller allowed")
	}
	ctx, c := context.WithTimeout(context.Background(), 3*time.Second)
	defer c()
	bytes := []byte{0, 1, 2, 255, 0, 4}
	if e := p.Write(ctx, websocket.MessageBinary, bytes); e != nil {
		t.Fatal(e)
	}
	typ, got, e := a.Read(ctx)
	if e != nil || typ != websocket.MessageBinary || string(got) != string(bytes) {
		t.Fatal("relay changed opaque bytes", e)
	}
	if e := a.Write(ctx, websocket.MessageBinary, bytes); e != nil {
		t.Fatal(e)
	}
	_, got, e = p.Read(ctx)
	if e != nil || string(got) != string(bytes) {
		t.Fatal(e)
	}
}
func TestTextAndOversizeCloseBothEnds(t *testing.T) {
	for _, typ := range []websocket.MessageType{websocket.MessageText, websocket.MessageBinary} {
		t.Run(string(rune(typ)), func(t *testing.T) {
			_, s := fixture(t, defaults())
			a, _, _ := dial(s, headers("agent", "control"))
			defer a.CloseNow()
			p, _, _ := dial(s, headers("phone", "control"))
			defer p.CloseNow()
			bytes := []byte("text")
			if typ == websocket.MessageBinary {
				bytes = make([]byte, 65537)
			}
			ctx, c := context.WithTimeout(context.Background(), 4*time.Second)
			defer c()
			p.Write(ctx, typ, bytes)
			if _, _, e := a.Read(ctx); e == nil {
				t.Fatal("invalid frame forwarded")
			}
		})
	}
}
func TestChannelIsolationAndRevocation(t *testing.T) {
	r, s := fixture(t, defaults())
	a, _, _ := dial(s, headers("agent", "control"))
	defer a.CloseNow()
	m, _, _ := dial(s, headers("agent", "media"))
	defer m.CloseNow()
	p, _, _ := dial(s, headers("phone", "control"))
	defer p.CloseNow()
	req, _ := http.NewRequest(http.MethodPost, s.URL+"/revoke", nil)
	req.Header = headers("agent", "control")
	response, e := s.Client().Do(req)
	if e != nil {
		t.Fatal(e)
	}
	response.Body.Close()
	if response.StatusCode != 204 {
		t.Fatal("revocation failed")
	}
	if _, ok := r.authenticate(req); ok {
		t.Fatal("revoked credential remains usable")
	}
	c, response, e := dial(s, headers("phone", "control"))
	if c != nil {
		c.CloseNow()
	}
	if e == nil || response.StatusCode != 403 {
		t.Fatal("revoked phone could reconnect")
	}
}
func TestSimultaneousDisconnectsLeaveNoPair(t *testing.T) {
	r, s := fixture(t, defaults())
	for i := 0; i < 16; i++ {
		a, _, e := dial(s, headers("agent", "control"))
		if e != nil {
			t.Fatal(e)
		}
		p, _, e := dial(s, headers("phone", "control"))
		if e != nil {
			t.Fatal(e)
		}
		var wg sync.WaitGroup
		wg.Add(2)
		go func() { defer wg.Done(); a.CloseNow() }()
		go func() { defer wg.Done(); p.CloseNow() }()
		wg.Wait()
		deadline := time.Now().Add(time.Second)
		for {
			r.mu.Lock()
			count := len(r.pairs)
			r.mu.Unlock()
			if count == 0 {
				break
			}
			if time.Now().After(deadline) {
				t.Fatal("pair leaked")
			}
			time.Sleep(time.Millisecond)
		}
	}
}
func TestConnectionLimit(t *testing.T) {
	opts := defaults()
	opts.MaxPairs = 1
	_, s := fixture(t, opts)
	a, _, _ := dial(s, headers("agent", "control"))
	defer a.CloseNow()
	c, r, e := dial(s, headers("agent", "media"))
	if c != nil {
		c.CloseNow()
	}
	if e == nil || r.StatusCode != 429 {
		t.Fatal("limit not enforced")
	}
}
