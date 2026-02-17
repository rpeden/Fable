package fable.library;

import java.util.concurrent.ExecutorService;
import java.util.concurrent.ForkJoinPool;

public final class Trampoline {
    private static final int MAX_CALL_COUNT = 2000;
    private static final ExecutorService EXECUTOR = ForkJoinPool.commonPool();

    private int callCount;

    public boolean incrementAndCheck() {
        callCount += 1;
        return callCount >= MAX_CALL_COUNT;
    }

    public void hijack(Runnable action) {
        callCount = 0;
        EXECUTOR.submit(action);
    }
}
