package fable.library;

import java.util.concurrent.CompletableFuture;
import java.util.function.Function;

public final class AsyncModule {
    private AsyncModule() {}

    public static <T> Async<T> protectedCont(Async<T> computation) {
        return ctx -> {
            if (ctx.getCancelToken().isCancelled()) {
                ctx.onCancel(new OperationCanceledException());
                return;
            }

            if (ctx.getTrampoline().incrementAndCheck()) {
                ctx.getTrampoline().hijack(() -> {
                    try {
                        computation.invoke(ctx);
                    } catch (Exception ex) {
                        ctx.onError(ex);
                    }
                });
                return;
            }

            try {
                computation.invoke(ctx);
            } catch (Exception ex) {
                ctx.onError(ex);
            }
        };
    }

    public static <T> Async<T> protectedReturn(T value) {
        return protectedCont(ctx -> ctx.onSuccess(value));
    }

    public static <T, U> Async<U> protectedBind(Async<T> computation, Function<T, Async<U>> binder) {
        return protectedCont(ctx -> computation.invoke(new AsyncContext<>(
                ctx.getTrampoline(),
                ctx.getCancelToken(),
                result -> {
                    try {
                        binder.apply(result).invoke(ctx);
                    } catch (Exception ex) {
                        ctx.onError(ex);
                    }
                },
                ctx::onError,
                ctx::onCancel
        )));
    }

    public static <T> CompletableFuture<T> startAsCompletableFuture(Async<T> computation, CancellationToken cancelToken) {
        CompletableFuture<T> future = new CompletableFuture<>();
        Trampoline trampoline = new Trampoline();

        computation.invoke(new AsyncContext<>(
                trampoline,
                cancelToken,
                future::complete,
                future::completeExceptionally,
                ex -> future.cancel(true)
        ));

        return future;
    }
}
