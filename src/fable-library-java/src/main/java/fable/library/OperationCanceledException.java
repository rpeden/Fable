package fable.library;

public final class OperationCanceledException extends RuntimeException {
    public OperationCanceledException() {
        super("Operation canceled");
    }
}
