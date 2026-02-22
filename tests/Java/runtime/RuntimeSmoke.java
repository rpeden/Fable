import fable.library.FSharpList;
import fable.library.FSharpOption;
import fable.library.FSharpResult;
import fable.library.Map;
import fable.library.Seq;
import fable.library.Set;

public class RuntimeSmoke {
    public static void main(String[] args) {
        FSharpOption<Integer> opt = FSharpOption.some(42);
        if (!opt.isSome() || opt.getValue() != 42) {
            throw new RuntimeException("FSharpOption.some failed");
        }

        if (opt.isNone()) {
            throw new RuntimeException("FSharpOption.isNone failed");
        }

        FSharpResult<String, String> ok = FSharpResult.ok("done");
        if (!ok.isOk() || !"done".equals(ok.getOk())) {
            throw new RuntimeException("FSharpResult.ok failed");
        }

        FSharpResult<Integer, String> mapped = FSharpResult.map(x -> x + 1, FSharpResult.ok(1));
        if (!mapped.isOk() || mapped.getOk() != 2) {
            throw new RuntimeException("FSharpResult.map failed");
        }

        FSharpResult<Integer, String> mappedError = FSharpResult.mapError(x -> x + "!", FSharpResult.error("bad"));
        if (mappedError.isOk() || !"bad!".equals(mappedError.getError())) {
            throw new RuntimeException("FSharpResult.mapError failed");
        }

        FSharpList<Integer> list = FSharpList.cons(1, FSharpList.cons(2, FSharpList.empty()));
        if (list.isEmpty() || list.head() != 1 || list.tail().head() != 2) {
            throw new RuntimeException("FSharpList failed");
        }

        java.util.Map<String, Integer> map = Map.empty();
        map = Map.add(map, "k", 9);
        if (!map.containsKey("k") || map.get("k") != 9) {
            throw new RuntimeException("Map failed");
        }

        java.util.Set<String> set = Set.empty();
        set = Set.add(set, "a");
        if (!set.contains("a")) {
            throw new RuntimeException("Set failed");
        }

        java.lang.String joined = fable.library.String.join(",", java.util.Arrays.asList("a", "b"));
        if (!"a,b".equals(joined)) {
            throw new RuntimeException("String.join failed");
        }

        Iterable<Integer> mappedSeq = Seq.map(x -> x + 1, java.util.Arrays.asList(1, 2));
        java.util.Iterator<Integer> it = mappedSeq.iterator();
        if (!it.hasNext() || it.next() != 2 || !it.hasNext() || it.next() != 3) {
            throw new RuntimeException("Seq.map failed");
        }
    }
}
