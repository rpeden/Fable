package fable.library;

public final class Map {
    private Map() {}

    public static <TKey, TValue> java.util.Map<TKey, TValue> empty() {
        return new java.util.TreeMap<>();
    }

    public static <TKey, TValue> java.util.Map<TKey, TValue> add(java.util.Map<TKey, TValue> source, TKey key, TValue value) {
        java.util.Map<TKey, TValue> result = new java.util.TreeMap<>(source);
        result.put(key, value);
        return result;
    }
}
