package api

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
)

func TestWatchdogStatus_DecodesMap(t *testing.T) {
	mux := http.NewServeMux()
	mux.HandleFunc("/api/watchdog/status", func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("X-Api-Key") != "k" {
			w.WriteHeader(http.StatusUnauthorized)
			return
		}
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{
			"lobby": {"serverName":"lobby","isMonitoring":true,"wasRunning":true,"restartAttempts":2,
				"lastCrashTime":"2026-07-22T01:00:00Z","cooldownEndsAt":"2026-07-22T01:05:00Z"}
		}`))
	})
	srv := httptest.NewServer(mux)
	defer srv.Close()

	status, err := NewClient(srv.URL, "k").WatchdogStatus(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	s, ok := status["lobby"]
	if !ok {
		t.Fatal("lobby missing from status map")
	}
	if !s.IsMonitoring || s.RestartAttempts != 2 {
		t.Fatalf("fields not decoded: %+v", s)
	}
	if s.LastCrashTime == nil || s.CooldownEndsAt == nil {
		t.Fatal("timestamps not decoded")
	}
	if s.LastManualStopTime != nil {
		t.Fatal("absent timestamp should stay nil")
	}
}

func TestWatchdogCrashes_DecodesListAndPassesLimit(t *testing.T) {
	var gotLimit string
	mux := http.NewServeMux()
	mux.HandleFunc("/api/watchdog/crashes", func(w http.ResponseWriter, r *http.Request) {
		gotLimit = r.URL.Query().Get("limit")
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[
			{"id":1,"serverName":"survival","detectedAt":"2026-07-22T00:30:00Z","crashType":"OutOfMemory",
				"crashDetails":"heap","autoRestartAttempted":true,"autoRestartSucceeded":false}
		]`))
	})
	srv := httptest.NewServer(mux)
	defer srv.Close()

	events, err := NewClient(srv.URL, "k").WatchdogCrashes(context.Background(), 7)
	if err != nil {
		t.Fatal(err)
	}
	if gotLimit != "7" {
		t.Fatalf("limit not passed, got %q", gotLimit)
	}
	if len(events) != 1 || events[0].CrashType != "OutOfMemory" {
		t.Fatalf("crash not decoded: %+v", events)
	}
	if !events[0].AutoRestartAttempted || events[0].AutoRestartSucceeded {
		t.Fatal("restart flags not decoded")
	}
}

func TestActiveNotifications_DecodesAndFilters(t *testing.T) {
	var gotQuery string
	mux := http.NewServeMux()
	mux.HandleFunc("/api/v1/notifications", func(w http.ResponseWriter, r *http.Request) {
		gotQuery = r.URL.RawQuery
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`[
			{"id":5,"type":"warning","title":"Low TPS","message":"lobby at 12 tps",
				"createdAt":"2026-07-22T00:45:00Z","isRead":false,"serverName":"lobby"}
		]`))
	})
	srv := httptest.NewServer(mux)
	defer srv.Close()

	alerts, err := NewClient(srv.URL, "k").ActiveNotifications(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if gotQuery != "includeDismissed=false" {
		t.Fatalf("dismissed filter not sent, got %q", gotQuery)
	}
	if len(alerts) != 1 || alerts[0].Title != "Low TPS" || alerts[0].IsRead {
		t.Fatalf("alert not decoded: %+v", alerts)
	}
	if alerts[0].ServerName == nil || *alerts[0].ServerName != "lobby" {
		t.Fatal("serverName not decoded")
	}
}

func TestWatchdogStatus_AuthErrors(t *testing.T) {
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusForbidden)
	}))
	defer srv.Close()

	if _, err := NewClient(srv.URL, "bad").WatchdogStatus(context.Background()); err != ErrApiKeyInvalid {
		t.Fatalf("want ErrApiKeyInvalid, got %v", err)
	}
	if _, err := NewClient(srv.URL, "").WatchdogStatus(context.Background()); err != ErrApiKeyMissing {
		t.Fatalf("want ErrApiKeyMissing, got %v", err)
	}
}
