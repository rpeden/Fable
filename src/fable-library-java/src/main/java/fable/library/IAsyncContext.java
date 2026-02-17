package fable.library;

public interface IAsyncContext<T> {
    void onSuccess(T value);
    void onError(Exception error);
    void onCancel(OperationCanceledException error);
    CancellationToken getCancelToken();
    Trampoline getTrampoline();
}
