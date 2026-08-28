package tui

import (
	"testing"
)

func TestListenLogs_BatchesBufferedLines(t *testing.T) {
	logs := make(chan string, 10)
	errs := make(chan error, 1)
	for _, l := range []string{"a", "b", "c"} {
		logs <- l
	}
	m := TuiModel{LogState: LogState{LogsChan: logs, LogErrsChan: errs}}

	msg := m.ListenLogsCmd()()
	batch, ok := msg.(LogLinesMsg)
	if !ok {
		t.Fatalf("want LogLinesMsg, got %T", msg)
	}
	if len(batch.Lines) != 3 || batch.Lines[0] != "a" || batch.Lines[2] != "c" {
		t.Fatalf("burst not drained in order: %v", batch.Lines)
	}
}

func TestListenLogs_ClosedChannelSignalsClose(t *testing.T) {
	logs := make(chan string)
	errs := make(chan error, 1)
	close(logs)
	m := TuiModel{LogState: LogState{LogsChan: logs, LogErrsChan: errs}}

	if _, ok := m.ListenLogsCmd()().(LogStreamClosedMsg); !ok {
		t.Fatal("closed channel must yield LogStreamClosedMsg")
	}
}

func TestListenLogs_CloseDuringDrainDeliversCollectedLines(t *testing.T) {
	logs := make(chan string, 2)
	errs := make(chan error, 1)
	logs <- "last"
	close(logs)
	m := TuiModel{LogState: LogState{LogsChan: logs, LogErrsChan: errs}}

	msg := m.ListenLogsCmd()()
	batch, ok := msg.(LogLinesMsg)
	if !ok {
		t.Fatalf("want LogLinesMsg, got %T", msg)
	}
	if len(batch.Lines) != 1 || batch.Lines[0] != "last" {
		t.Fatalf("lines lost on close: %v", batch.Lines)
	}
	// The re-armed listener then observes the close.
	m.LogsChan = logs
	if _, ok := m.ListenLogsCmd()().(LogStreamClosedMsg); !ok {
		t.Fatal("re-armed listener must observe the close")
	}
}

func TestUpdate_CleanCloseRetriesWithDelayAndCap(t *testing.T) {
	m := TuiModel{LogState: LogState{LogsActive: true}}

	// First MaxLogRetries closes schedule a delayed retry.
	for i := 0; i < MaxLogRetries; i++ {
		out, cmd := m.Update(LogStreamClosedMsg{})
		m = out.(TuiModel)
		if cmd == nil {
			t.Fatalf("close %d: expected a scheduled retry", i+1)
		}
	}
	if m.LogRetries != MaxLogRetries {
		t.Fatalf("retry counter wrong: %d", m.LogRetries)
	}

	// The next close gives up.
	out, cmd := m.Update(LogStreamClosedMsg{})
	m = out.(TuiModel)
	if cmd != nil {
		t.Fatal("retries exhausted: no further retry expected")
	}

	// Receiving lines resets the counter.
	out, _ = m.Update(LogLinesMsg{Lines: []string{"x"}})
	m = out.(TuiModel)
	if m.LogRetries != 0 {
		t.Fatal("data receipt must reset the retry counter")
	}
	if len(m.Logs) != 1 {
		t.Fatal("batched lines must be appended")
	}
}

func TestUpdate_CleanCloseDoesNotRetryWhenStopped(t *testing.T) {
	for _, m := range []TuiModel{
		{LogState: LogState{LogsActive: true}, ConnectionState: ConnectionState{ContainersStopped: true}},
		{LogState: LogState{LogsActive: false}},
		{LogState: LogState{LogsActive: true}, Quitting: true},
	} {
		if _, cmd := m.Update(LogStreamClosedMsg{}); cmd != nil {
			t.Fatalf("no retry expected for %+v", m)
		}
	}
}
