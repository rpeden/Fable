package fable.library;

import java.util.NoSuchElementException;

public final class FSharpList<T> {
    private static final FSharpList<?> EMPTY = new FSharpList<>();

    private final boolean isEmpty;
    private final T head;
    private final FSharpList<T> tail;

    private FSharpList() {
        this.isEmpty = true;
        this.head = null;
        this.tail = null;
    }

    private FSharpList(T head, FSharpList<T> tail) {
        this.isEmpty = false;
        this.head = head;
        this.tail = tail;
    }

    @SuppressWarnings("unchecked")
    public static <T> FSharpList<T> empty() {
        return (FSharpList<T>) EMPTY;
    }

    public static <T> FSharpList<T> cons(T head, FSharpList<T> tail) {
        return new FSharpList<>(head, tail == null ? empty() : tail);
    }

    public boolean isEmpty() {
        return isEmpty;
    }

    public T head() {
        if (isEmpty) {
            throw new NoSuchElementException("The list is empty");
        }

        return head;
    }

    public FSharpList<T> tail() {
        if (isEmpty) {
            throw new NoSuchElementException("The list is empty");
        }

        return tail;
    }
}
