package fable.library;

import java.util.function.Function;

public abstract class FSharpResult<TOk, TError> {
    public abstract boolean isOk();

    public abstract TOk getOk();

    public abstract TError getError();

    public static <TOk, TError> FSharpResult<TOk, TError> ok(TOk value) {
        return new Ok<>(value);
    }

    public static <TOk, TError> FSharpResult<TOk, TError> error(TError value) {
        return new Error<>(value);
    }

    public static <TOk, TError, TOut> FSharpResult<TOut, TError> map(
            Function<TOk, TOut> mapper,
            FSharpResult<TOk, TError> result) {
        if (result.isOk()) {
            return ok(mapper.apply(result.getOk()));
        }

        return error(result.getError());
    }

    public static <TOk, TError, TOutError> FSharpResult<TOk, TOutError> mapError(
            Function<TError, TOutError> mapper,
            FSharpResult<TOk, TError> result) {
        if (result.isOk()) {
            return ok(result.getOk());
        }

        return error(mapper.apply(result.getError()));
    }

    private static final class Ok<TOk, TError> extends FSharpResult<TOk, TError> {
        private final TOk value;

        private Ok(TOk value) {
            this.value = value;
        }

        @Override
        public boolean isOk() {
            return true;
        }

        @Override
        public TOk getOk() {
            return value;
        }

        @Override
        public TError getError() {
            throw new IllegalStateException("Result is Ok");
        }
    }

    private static final class Error<TOk, TError> extends FSharpResult<TOk, TError> {
        private final TError value;

        private Error(TError value) {
            this.value = value;
        }

        @Override
        public boolean isOk() {
            return false;
        }

        @Override
        public TOk getOk() {
            throw new IllegalStateException("Result is Error");
        }

        @Override
        public TError getError() {
            return value;
        }
    }
}
