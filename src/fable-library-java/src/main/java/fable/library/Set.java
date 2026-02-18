package fable.library;

public final class Set {
    private Set() {}

    public static <T> java.util.Set<T> empty() {
        return new java.util.TreeSet<>();
    }

    public static <T> java.util.Set<T> add(java.util.Set<T> source, T value) {
        java.util.Set<T> result = new java.util.TreeSet<>(source);
        result.add(value);
        return result;
    }
}
