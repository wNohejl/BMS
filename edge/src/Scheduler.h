// Scheduler.h — time-based task scheduling for the engine loop: sensor reads,
// optimizer passes, status heartbeats. Single-threaded; runDue() is called from
// the engine loop with the current wall clock.
#pragma once
#include <functional>
#include <vector>

class Scheduler {
public:
    using Task = std::function<void(long long nowMs)>;

    void every(long long intervalMs, Task task, long long startDelayMs = 0);
    void runDue(long long nowMs);

private:
    struct Entry {
        long long intervalMs;
        long long startDelayMs;
        long long nextDueMs = -1; // -1 = not yet initialized against the clock
        Task task;
    };
    std::vector<Entry> entries_;
};
