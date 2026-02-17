package fable.library;

@FunctionalInterface
public interface Async<T> {
    void invoke(IAsyncContext<T> context);
}
