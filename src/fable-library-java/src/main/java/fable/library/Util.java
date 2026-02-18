package fable.library;

import java.util.Objects;

public final class Util {
    private Util() {}

    public static boolean structuralEquals(Object left, Object right) {
        return Objects.deepEquals(left, right);
    }

    public static int structuralHash(Object value) {
        return Objects.hashCode(value);
    }

    @SuppressWarnings({"rawtypes", "unchecked"})
    public static int structuralCompareTo(Object left, Object right) {
        if (left == right) {
            return 0;
        }

        if (left == null) {
            return -1;
        }

        if (right == null) {
            return 1;
        }

        if (left instanceof Comparable && right.getClass().isAssignableFrom(left.getClass())) {
            Comparable comparable = (Comparable) left;
            return comparable.compareTo(right);
        }

        return left.toString().compareTo(right.toString());
    }
}
