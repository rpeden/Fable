package fable.library;

import java.util.function.Consumer;

public final class AsyncContext<T> implements IAsyncContext<T> {
    private final Trampoline trampoline;
    private final CancellationToken cancelToken;
    private final Consumer<T> onSuccess;
    private final Consumer<Exception> onError;
    private final Consumer<OperationCanceledException> onCancel;

    public AsyncContext(
            Trampoline trampoline,
            CancellationToken cancelToken,
            Consumer<T> onSuccess,
            Consumer<Exception> onError,
            Consumer<OperationCanceledException> onCancel) {
        this.trampoline = trampoline;
        this.cancelToken = cancelToken;
        this.onSuccess = onSuccess;
        this.onError = onError;
        this.onCancel = onCancel;
    }

    @Override
    public void onSuccess(T value) {
        onSuccess.accept(value);
    }

    @Override
    public void onError(Exception error) {
        onError.accept(error);
    }

    @Override
    public void onCancel(OperationCanceledException error) {
        onCancel.accept(error);
    }

    @Override
    public CancellationToken getCancelToken() {
        return cancelToken;
    }

    @Override
    public Trampoline getTrampoline() {
        return trampoline;
    }
}
