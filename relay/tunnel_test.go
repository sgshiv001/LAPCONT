// LapCont — Tests — Nested mutual TLS through the real opaque relay, encrypted records and wrong identity
// License: MIT
package main

import (
	"bytes"
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/sha256"
	"crypto/tls"
	"crypto/x509"
	"crypto/x509/pkix"
	"errors"
	"github.com/coder/websocket"
	"io"
	"math/big"
	"net"
	"sync"
	"testing"
	"time"
)

type tlsCarrier struct {
	conn    *websocket.Conn
	ctx     context.Context
	pending []byte
	mu      sync.Mutex
	records [][]byte
}

func (c *tlsCarrier) Read(b []byte) (int, error) {
	if len(c.pending) == 0 {
		kind, data, err := c.conn.Read(c.ctx)
		if err != nil {
			return 0, err
		}
		if kind != websocket.MessageBinary {
			return 0, errors.New("wrong carrier type")
		}
		c.pending = data
	}
	n := copy(b, c.pending)
	c.pending = c.pending[n:]
	return n, nil
}
func (c *tlsCarrier) Write(b []byte) (int, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.records = append(c.records, append([]byte(nil), b...))
	if err := c.conn.Write(c.ctx, websocket.MessageBinary, b); err != nil {
		return 0, err
	}
	return len(b), nil
}
func (c *tlsCarrier) Close() error                     { return c.conn.CloseNow() }
func (c *tlsCarrier) LocalAddr() net.Addr              { return &net.TCPAddr{} }
func (c *tlsCarrier) RemoteAddr() net.Addr             { return &net.TCPAddr{} }
func (c *tlsCarrier) SetDeadline(time.Time) error      { return nil }
func (c *tlsCarrier) SetReadDeadline(time.Time) error  { return nil }
func (c *tlsCarrier) SetWriteDeadline(time.Time) error { return nil }
func certificate(t *testing.T) tls.Certificate {
	t.Helper()
	key, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	serial, err := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 120))
	if err != nil {
		t.Fatal(err)
	}
	template := &x509.Certificate{SerialNumber: serial, Subject: pkix.Name{CommonName: "Ephemeral LapCont test identity"}, NotBefore: time.Now().Add(-time.Minute), NotAfter: time.Now().Add(time.Hour), KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth, x509.ExtKeyUsageClientAuth}, DNSNames: []string{"localhost"}}
	der, err := x509.CreateCertificate(rand.Reader, template, template, &key.PublicKey, key)
	if err != nil {
		t.Fatal(err)
	}
	return tls.Certificate{Certificate: [][]byte{der}, PrivateKey: key}
}
func TestNestedMutualTLSAndWrongIdentity(t *testing.T) {
	for _, wrong := range []bool{false, true} {
		t.Run(map[bool]string{false: "encrypted_round_trip", true: "wrong_phone_rejected"}[wrong], func(t *testing.T) {
			_, s := fixture(t, defaults())
			agent, _, err := dial(s, headers("agent", "control"))
			if err != nil {
				t.Fatal(err)
			}
			defer agent.CloseNow()
			phone, _, err := dial(s, headers("phone", "control"))
			if err != nil {
				t.Fatal(err)
			}
			defer phone.CloseNow()
			ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
			defer cancel()
			pcCert, phoneCert := certificate(t), certificate(t)
			actual := phoneCert
			if wrong {
				actual = certificate(t)
			}
			expectedPhone := sha256.Sum256(phoneCert.Certificate[0])
			expectedPc := sha256.Sum256(pcCert.Certificate[0])
			agentWire := &tlsCarrier{conn: agent, ctx: ctx}
			phoneWire := &tlsCarrier{conn: phone, ctx: ctx}
			server := tls.Server(agentWire, &tls.Config{Certificates: []tls.Certificate{pcCert}, MinVersion: tls.VersionTLS12, ClientAuth: tls.RequireAnyClientCert, VerifyConnection: func(state tls.ConnectionState) error {
				if len(state.PeerCertificates) != 1 || sha256.Sum256(state.PeerCertificates[0].Raw) != expectedPhone {
					return errors.New("wrong phone pin")
				}
				return nil
			}})
			// Test-only exact leaf verifier: the self-signed fixture is never installed into an OS trust store.
			client := tls.Client(phoneWire, &tls.Config{Certificates: []tls.Certificate{actual}, MinVersion: tls.VersionTLS12, InsecureSkipVerify: true, VerifyConnection: func(state tls.ConnectionState) error {
				if len(state.PeerCertificates) != 1 || sha256.Sum256(state.PeerCertificates[0].Raw) != expectedPc {
					return errors.New("wrong PC pin")
				}
				return nil
			}})
			payload := []byte(`{"protocol":"LPC1","command":"status","private_test_marker":"relay_must_not_read_this"}`)
			done := make(chan error, 1)
			go func() {
				if err := server.HandshakeContext(ctx); err != nil {
					done <- err
					return
				}
				received := make([]byte, len(payload))
				_, err := io.ReadFull(server, received)
				if err == nil && !bytes.Equal(received, payload) {
					err = errors.New("payload changed")
				}
				if err == nil {
					_, err = server.Write([]byte("verified"))
				}
				done <- err
			}()
			err = client.HandshakeContext(ctx)
			if wrong {
				if err == nil {
					_, err = client.Write(payload)
				}
				if rejected := <-done; rejected == nil {
					t.Fatal("wrong phone authenticated")
				}
				return
			}
			if err != nil {
				t.Fatal(err)
			}
			if _, err = client.Write(payload); err != nil {
				t.Fatal(err)
			}
			response := make([]byte, 8)
			if _, err = io.ReadFull(client, response); err != nil {
				t.Fatal(err)
			}
			if string(response) != "verified" {
				t.Fatal("wrong protected response")
			}
			if err = <-done; err != nil {
				t.Fatal(err)
			}
			phoneWire.mu.Lock()
			defer phoneWire.mu.Unlock()
			for _, record := range phoneWire.records {
				if bytes.Contains(record, []byte("relay_must_not_read_this")) {
					t.Fatal("relay received plaintext business content")
				}
			}
		})
	}
}
