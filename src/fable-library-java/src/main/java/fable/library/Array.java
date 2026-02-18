package fable.library;

import java.util.ArrayList;
import java.util.List;
import java.util.function.Function;

public final class Array {
    private Array() {}

    public static <T, U> List<U> map(List<T> source, Function<T, U> mapper) {
        List<U> result = new ArrayList<>(source.size());

        for (T item : source) {
            result.add(mapper.apply(item));
        }

        return result;
    }

    public static <T> List<T> append(List<T> left, List<T> right) {
        List<T> result = new ArrayList<>(left.size() + right.size());
        result.addAll(left);
        result.addAll(right);
        return result;
    }
}
