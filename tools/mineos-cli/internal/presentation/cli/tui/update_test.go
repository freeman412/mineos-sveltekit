package tui

import "testing"

// HealthCheckedMsg is the only thing that flips the health badge.
func TestUpdate_HealthCheckedSetsHealthy(t *testing.T) {
	m := TuiModel{}
	out, _ := m.Update(HealthCheckedMsg{Healthy: true})
	if !out.(TuiModel).Healthy {
		t.Fatal("expected Healthy=true after a successful HealthCheckedMsg")
	}
	out2, _ := out.(TuiModel).Update(HealthCheckedMsg{Healthy: false})
	if out2.(TuiModel).Healthy {
		t.Fatal("expected Healthy=false after a failed HealthCheckedMsg")
	}
}

// The refresh loop must keep re-arming (non-nil cmd) and stopped != healthy.
func TestUpdate_HealthTickReschedulesAndStoppedIsUnhealthy(t *testing.T) {
	m := TuiModel{ConnectionState: ConnectionState{ContainersStopped: true, Healthy: true}}
	out, cmd := m.Update(HealthTickMsg{})
	if out.(TuiModel).Healthy {
		t.Fatal("stopped containers must not report healthy")
	}
	if cmd == nil {
		t.Fatal("health tick must always reschedule (non-nil cmd)")
	}
}

// While ready, a tick issues refresh work (and still re-arms).
func TestUpdate_HealthTickWhileReadyRefreshes(t *testing.T) {
	m := TuiModel{ConnectionState: ConnectionState{ConfigReady: true}}
	_, cmd := m.Update(HealthTickMsg{})
	if cmd == nil {
		t.Fatal("expected refresh commands while ready")
	}
}
