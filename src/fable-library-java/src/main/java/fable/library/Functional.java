package fable.library;

public final class Functional {
    private Functional() {}

    @FunctionalInterface
    public interface Func0<R> {
        R invoke();
    }

    @FunctionalInterface
    public interface Func1<T1, R> {
        R invoke(T1 arg1);
    }

    @FunctionalInterface
    public interface Func2<T1, T2, R> {
        R invoke(T1 arg1, T2 arg2);
    }

    @FunctionalInterface
    public interface Func3<T1, T2, T3, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3);
    }

    @FunctionalInterface
    public interface Func4<T1, T2, T3, T4, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    }

    @FunctionalInterface
    public interface Func5<T1, T2, T3, T4, T5, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
    }

    @FunctionalInterface
    public interface Func6<T1, T2, T3, T4, T5, T6, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
    }

    @FunctionalInterface
    public interface Func7<T1, T2, T3, T4, T5, T6, T7, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
    }

    @FunctionalInterface
    public interface Func8<T1, T2, T3, T4, T5, T6, T7, T8, R> {
        R invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
    }

    @FunctionalInterface
    public interface Action0 {
        void invoke();
    }

    @FunctionalInterface
    public interface Action1<T1> {
        void invoke(T1 arg1);
    }

    @FunctionalInterface
    public interface Action2<T1, T2> {
        void invoke(T1 arg1, T2 arg2);
    }

    @FunctionalInterface
    public interface Action3<T1, T2, T3> {
        void invoke(T1 arg1, T2 arg2, T3 arg3);
    }

    @FunctionalInterface
    public interface Action4<T1, T2, T3, T4> {
        void invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    }

    @FunctionalInterface
    public interface Action5<T1, T2, T3, T4, T5> {
        void invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5);
    }

    @FunctionalInterface
    public interface Action6<T1, T2, T3, T4, T5, T6> {
        void invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6);
    }

    @FunctionalInterface
    public interface Action7<T1, T2, T3, T4, T5, T6, T7> {
        void invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7);
    }

    @FunctionalInterface
    public interface Action8<T1, T2, T3, T4, T5, T6, T7, T8> {
        void invoke(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8);
    }
}
