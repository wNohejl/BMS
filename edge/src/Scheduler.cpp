#include "Scheduler.h"

void Scheduler::every(long long intervalMs, Task task, long long startDelayMs) {
    entries_.push_back(Entry{intervalMs, startDelayMs, -1, std::move(task)});
}

void Scheduler::runDue(long long nowMs) {
    for (auto& entry : entries_) {
        if (entry.nextDueMs < 0) {
            entry.nextDueMs = nowMs + entry.startDelayMs;
        }
        if (nowMs >= entry.nextDueMs) {
            entry.task(nowMs);
            // Schedule from "now", not from the missed slot, so a slow tick
            // doesn't cause a burst of catch-up runs.
            entry.nextDueMs = nowMs + entry.intervalMs;
        }
    }
}
