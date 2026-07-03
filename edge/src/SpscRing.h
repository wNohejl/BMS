// SpscRing.h — single-producer single-consumer lock-free ring buffer.
// Producer: the physics thread pushing sensor samples as the model evolves.
// Consumer: the control loop draining them each tick.
// Wait-free on both sides; correctness rests on the acquire/release pairs:
// the producer's release-store of head publishes the written slot, the
// consumer's release-store of tail returns the slot to the producer.
#pragma once
#include <array>
#include <atomic>
#include <cstddef>

template <typename T, size_t Capacity>
class SpscRing {
    static_assert((Capacity & (Capacity - 1)) == 0, "Capacity must be a power of two");

public:
    // Returns false when full — the producer drops the sample (freshness beats
    // completeness for periodic telemetry; the next sample arrives in 100ms).
    bool push(const T& item) {
        const size_t head = head_.load(std::memory_order_relaxed);
        const size_t next = (head + 1) & kMask;
        if (next == tail_.load(std::memory_order_acquire)) {
            return false; // full
        }
        buffer_[head] = item;
        head_.store(next, std::memory_order_release);
        return true;
    }

    bool pop(T& out) {
        const size_t tail = tail_.load(std::memory_order_relaxed);
        if (tail == head_.load(std::memory_order_acquire)) {
            return false; // empty
        }
        out = buffer_[tail];
        tail_.store((tail + 1) & kMask, std::memory_order_release);
        return true;
    }

private:
    static constexpr size_t kMask = Capacity - 1;
    std::array<T, Capacity> buffer_{};
    std::atomic<size_t> head_{0};
    std::atomic<size_t> tail_{0};
};
