package fable.library;

public final class Types {
    private Types() {}

    public static final class Unit {
        public static final Unit INSTANCE = new Unit();

        private Unit() {}

        @Override
        public java.lang.String toString() {
            return "()";
        }
    }
}
