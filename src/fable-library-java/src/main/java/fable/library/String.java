package fable.library;

public final class String {
    private String() {}

    public static java.lang.String concat(java.lang.String left, java.lang.String right) {
        return left + right;
    }

    public static java.lang.String join(java.lang.String separator, Iterable<?> values) {
        java.lang.StringBuilder builder = new java.lang.StringBuilder();
        boolean first = true;

        for (Object value : values) {
            if (!first) {
                builder.append(separator);
            }

            builder.append(value == null ? "null" : value.toString());
            first = false;
        }

        return builder.toString();
    }
}
