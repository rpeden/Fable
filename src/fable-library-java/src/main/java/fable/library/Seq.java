package fable.library;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

public final class Seq {
    private Seq() {}

    public static <T, U> Iterable<U> map(Iterable<T> source, Function<T, U> mapper) {
        List<U> result = new ArrayList<>();

        for (T item : source) {
            result.add(mapper.apply(item));
        }

        return result;
    }

    public static <T> FSharpList<T> toList(Iterable<T> source) {
        List<T> buffer = new ArrayList<>();

        for (T item : source) {
            buffer.add(item);
        }

        FSharpList<T> result = FSharpList.empty();

        for (int i = buffer.size() - 1; i >= 0; i--) {
            result = FSharpList.cons(buffer.get(i), result);
        }

        return result;
    }
}
