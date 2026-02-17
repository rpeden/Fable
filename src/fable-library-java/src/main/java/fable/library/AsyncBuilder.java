package fable.library;

import java.util.function.Function;
import java.util.function.Supplier;

public final class AsyncBuilder {
    public <T, U> Async<U> Bind(Async<T> computation, Function<T, Async<U>> binder) {
        return AsyncModule.protectedBind(computation, binder);
    }

    public <T> Async<T> Return(T value) {
        return AsyncModule.protectedReturn(value);
    }

    public <T> Async<T> ReturnFrom(Async<T> computation) {
        return computation;
    }

    public <T> Async<T> Delay(Supplier<Async<T>> generator) {
        return AsyncModule.protectedCont(ctx -> generator.get().invoke(ctx));
    }
}
