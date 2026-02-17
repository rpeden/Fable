package fable.library;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicInteger;

public final class CancellationToken {
    private final AtomicInteger listenerId = new AtomicInteger(0);
    private final Map<Integer, Runnable> listeners = new ConcurrentHashMap<>();
    private volatile boolean cancelled;

    public boolean isCancelled() {
        return cancelled;
    }

    public void cancel() {
        if (cancelled) {
            return;
        }

        cancelled = true;
        for (Runnable listener : listeners.values()) {
            listener.run();
        }
        listeners.clear();
    }

    public int addListener(Runnable listener) {
        int id = listenerId.incrementAndGet();
        listeners.put(id, listener);
        if (cancelled) {
            listener.run();
            listeners.remove(id);
        }
        return id;
    }

    public void removeListener(int id) {
        listeners.remove(id);
    }
}
