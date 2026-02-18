package fable.library;

public abstract class FSharpOption<T> {
    private static final None<?> NONE = new None<>();

    public abstract boolean isSome();

    public final boolean isNone() {
        return !isSome();
    }

    public abstract T getValue();

    @SuppressWarnings("unchecked")
    public static <T> FSharpOption<T> none() {
        return (FSharpOption<T>) NONE;
    }

    public static <T> FSharpOption<T> some(T value) {
        return new Some<>(value);
    }

    private static final class None<T> extends FSharpOption<T> {
        @Override
        public boolean isSome() {
            return false;
        }

        @Override
        public T getValue() {
            throw new IllegalStateException("Cannot get value from None");
        }
    }

    private static final class Some<T> extends FSharpOption<T> {
        private final T value;

        private Some(T value) {
            this.value = value;
        }

        @Override
        public boolean isSome() {
            return true;
        }

        @Override
        public T getValue() {
            return value;
        }
    }
}
