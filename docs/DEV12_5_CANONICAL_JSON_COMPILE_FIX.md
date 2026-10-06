# DEV12.5 — Canonical JSON compile fix

Fixed `CS1503` in `PgnCanonicalJsonV1.WriteCanonicalNumber`.

Before:

```csharp
raw.StartsWith('-', StringComparison.Ordinal)
```

`string.StartsWith(char, StringComparison)` is not a valid overload.

After:

```csharp
raw.StartsWith("-", StringComparison.Ordinal)
```

No license canonicalization behavior changes; this is a compile-only correction.
