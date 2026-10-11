namespace Zfl.Core;

/// Mod yükleme sırası: kullanıcının sırası + modların kendi kuralları (require, loadModAfter, loadModBefore).
public static class LoadOrder {
  static readonly StringComparer C = StringComparer.OrdinalIgnoreCase;

  /// Kayıtlı sırayı korur; listede olmayan yeni anahtarları varsayılan sıradaki bir öncekinin hemen arkasına koyar.
  /// Artık var olmayan anahtarlar düşer.
  public static List<string> Merge(IReadOnlyList<string> defaults, IReadOnlyList<string> saved) {
    var result = saved.Where(s => defaults.Contains(s, C)).Distinct(C).ToList();
    for (int i = 0; i < defaults.Count; i++) {
      var key = defaults[i];
      if (result.Contains(key, C)) continue;
      var prev = defaults.Take(i).LastOrDefault(p => result.Contains(p, C));
      result.Insert(prev is null ? 0 : result.FindIndex(x => C.Equals(x, prev)) + 1, key);
    }
    return result;
  }

  /// Verilen sırayı olabildiğince korur, ama bir mod ihtiyaç duyduğu ya da arkasından yüklenmek istediği
  /// modlardan önce gelmez. Döngü varsa sıradakini olduğu gibi alır.
  public static List<ModInfo> Sort(IReadOnlyList<ModInfo> mods) {
    var list = mods.DistinctBy(m => m.Id, C).ToList();
    var ids = list.Select(m => m.Id).ToHashSet(C);
    var mustFollow = list.ToDictionary(m => m.Id, _ => new HashSet<string>(C), C);
    foreach (var m in list) {
      foreach (var r in m.Requires.Concat(m.LoadAfter)) if (ids.Contains(r) && !C.Equals(r, m.Id)) mustFollow[m.Id].Add(r);
      foreach (var r in m.LoadBefore) if (ids.Contains(r) && !C.Equals(r, m.Id)) mustFollow[r].Add(m.Id);
    }
    var done = new HashSet<string>(C);
    var result = new List<ModInfo>();
    while (result.Count < list.Count) {
      var next = list.FirstOrDefault(m => !done.Contains(m.Id) && mustFollow[m.Id].All(done.Contains))
                 ?? list.First(m => !done.Contains(m.Id));
      done.Add(next.Id); result.Add(next);
    }
    return result;
  }

  /// Birlikte açık olmaması gereken mod çiftleri (mod.info "incompatible").
  public static List<(ModInfo Mod, string Other)> Conflicts(IReadOnlyList<ModInfo> mods) {
    var ids = mods.Select(m => m.Id).ToHashSet(C);
    return mods.SelectMany(m => m.Incompatible.Where(ids.Contains).Select(o => (m, o))).ToList();
  }
}
