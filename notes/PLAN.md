# SharpLibm — разбор доноров и раскладка

Состояние: 2026-10-09. Доноры — `work/math/donors/`, тесты — `work/math/tests/`
(скрипт `fetch_tests.sh`, журнал `fetch_tests.log`). Ничего ещё не перенесено.

## Устройство (по схеме пользователя)

1. **SharpLibm** (сабмодуль `SharpLibm/`, MIT): чистый C#, собирается под
   NativeAOT 8 NoStdLib, базовые тела на SSE2. Ничего не знает ни про
   патчинг, ни про SharpOS. Работает на любом x64.
2. **Патчер** — в SharpOS, на механизме byte-патчеров (`ByRefAssignRefPatcher`
   и т. п.): при загрузке читает CPUID/XCR0 и подменяет входы:
   - SSE4.1 (CPUID.1:ECX бит 19) → `floor, ceil, trunc, rint, modf` через `roundsd`;
   - FMA (пять бит CPUID + XCR0) → `fma`, `fmaf` через `vfmadd`.
   Без патчера всё работает так же, только медленнее.

Требование к библиотеке ради патчера: подменяемые функции — отдельные
не встраиваемые методы (`NoInlining`) с телом не короче заплатки.

## Откуда какая функция

| Функции | Донор | Лицензия | Что делать |
|---|---|---|---|
| ~47 double / ~48 float трансцендентных (sin…tanh, asin…atanh, exp/exp2/exp10/expm1, log/log2/log10/log1p, pow, atan2, hypot, cbrt, erf/erfc, tgamma, lgamma, *pi-функции, rsqrt, compound) | **CoreMathSharp** (порт CORE-MATH, корректное округление) | MIT (+ CORE-MATH MIT, musl MIT для fma) | Перенос с правками ниже |
| `fma`, `fmaf` (программные, корректные) | CoreMathSharp `FusedMultiplyAdd*.cs` (musl + hal-04575249) | MIT | Базовое тело; патчер → `vfmadd` |
| `floor, ceil, trunc, round, roundeven, rint, modf` | go2cs `floor.cs`, `modf.cs` (+ алгоритм `floor.c` Cosmos) | BSD-3 (Go) | Бит-маски; патчер → `roundsd` |
| `fmod`, `fmodf` | Cosmos gen3 `Runtime/Math.cs:229-431` (e_fmod) | BSD-3 + Sun | Почти как есть; `fmodf = (float)fmod` точно |
| `remainder` | go2cs `remainder.cs` (msun) | BSD-3 + Sun | Чистка golib |
| `remquo` | GeographicLib.NET `CMathManaged.cs:24-123` (musl) | MIT | Почти как есть; дописать атрибуцию musl |
| `frexp, ldexp/scalbn/scalbln, ilogb, logb, nextafter` | C.math.NET `math.cs` | MIT | Как есть, **кроме** `signbit`/`copysign` (переворот знака NaN) и `INT_MAX=32767` |
| Бессель `j0 j1 jn y0 y1 yn`, `erfinv` | go2cs (msun / AS241) | BSD-3 + Sun | Таблицы → `ReadOnlySpan`, `BigInteger`-константы → `const` |
| `sqrt` | инструкция `sqrtsd` (SSE2) | — | Решить способ (ниже) |
| sin/cos/tan классической линии fdlibm, `__kernel_rem_pio2` | NetWasm CoreLib | MIT + Sun | Справочно/запасной путь; CORE-MATH уже даёт 0,5 ulp |

Не брать: упрощённое в NetWasm `Math.cs` (Pow = Exp·Log, Cbrt, Asin через
Atan2, Log10 делением…), упрощённое в Cosmos (ceil/trunc через `(long)`,
Newton-sqrt, pow, atan2, гиперболические, ARM64 sin/cos), семантику знака
NaN из C.math.NET.

## Правки CoreMathSharp под NoStdLib

- `Helper.cs:30,37`, `HelperF.cs:18,25` (`Math.Floor/Truncate`) → свои
  бит-маски (на x64 в .NET 8–10 это вызовы `floor`/`modf`).
- `RoundEvenFinite` в 24 файлах: убрать ветки Avx/Sse41/AdvSimd, оставить
  программную (или `(x + 0x1.8p52) - 0x1.8p52`).
- `System.Runtime.Intrinsics` (≈87 мест): убрать; FMA — только программный
  путь (патчер даёт аппаратный).
- `readonly record struct` (`Uint128`, `Dint`, `Tint`, `Qint`) → обычные
  `struct` с конструкторами и `==`; 84 выражения `with` → конструкторы.
- `MemoryMarshal.Cast<double, ulong>` (6 мест) → таблицы `ReadOnlySpan<ulong>`.
- Исключения с интерполяцией (`Pow.cs:2144`, `Atan2.cs:391`,
  `CompoundF.cs:509`) → без форматирования.
- Удалить `ParseFormat*.cs` (Regex, только для тестов).
- `Polyfill.cs` переписать под свою std без `#if`; мёртвая ветка BigMul
  `Polyfill.cs:168-179` сломана.
- Таблицы — локальные `ReadOnlySpan<T> = [...]` (896 штук): у нас есть
  `RuntimeHelpers.CreateSpan` (оба `MinimalRuntime.cs`), данные лягут блобом.
- Проверить: приведения `(long)/(int)` double → встроенный `cvttsd2si`, а не
  помощник; `ulong → double` (`conv.r.un`).
- Нужно от std: `ValueTuple` (≈311 локальных функций), `Span`/`stackalloc`,
  `double.IsFinite/IsNaN/IsNormal/...`.

## Тесты (`work/math/tests/`, в репозиторий — отдельно от MIT-части)

| Набор | Для чего | Лицензия |
|---|---|---|
| CORE-MATH `.wc` (`core-math/src/**/*.wc`) | трудные случаи; эталон — C-оригинал/MPFR | MIT |
| CoreMathSharp `*.txt` | побитовые векторы C-версии (13 особых + 1024 случайных) | MIT |
| musl libc-test `src/math` | ошибка в ulp на вектор; группы sanity/special — MIT, `ucb` — Sun, `crlibm` — GPL | MIT / Sun / GPL |
| ARM optimized-routines `math/test` | направленные особые значения | MIT/Apache |
| Rust `libm-test` | граничные случаи, регрессии, допуски по функциям | MIT |
| LLVM-libc `libc/test/src/math` | smoke особых значений, TrickyInputs | Apache-LLVM |
| Berkeley TestFloat 3 | точные операции (sqrt, fma, rem, roundToInt) | BSD-3 |
| Go `src/math/*_test.go`, go2cs `all_test.cs` | Бессель, gamma, mod/remainder, frexp/ldexp, round | BSD-3 |
| GeographicLib `MathExTestData*.cs` | побитовые: remquo, frexp, scalbn, fma, hypot, log1p, expm1, log2, exp2 | MIT |
| C.math.NET `UnitTests/math.cs` | frexp/ilogb/nextafter (учесть их NaN-причуду) | MIT |
| FreeBSD msun tests | особые значения, fma, rem, next | BSD-2 |
| test262, V8, Jint | fdlibm-порты: гиперболические, cbrt, expm1, log1p | BSD / MIT |
| CPython `math_testcases.txt` | erf/gamma/lgamma/expm1/log1p/log2 | PSF |
| glibc `auto-libm-test-*` | ожидания по режимам округления | LGPL |
| OpenJDK StrictMath/Math | fdlibm-точные ожидания, худшие случаи | GPLv2 |
| crlibm, newlib, AMD aocl | трудные случаи / таблицы | GPL / newlib / BSD-3 |
| math_accuracy (Циммерман) | программы поиска худших случаев | см. репозиторий |

NetWasm-фикстуры — NetWasm Community License, не копировать; при нужде
пересчитать свои векторы.

Hex-float `double.Parse` не понимает → векторы `.wc`, libc-test, glibc
один раз переводятся в битовые шаблоны.

## Открытые вопросы

- Имена API: C (`sin`, `fmod`) или .NET (`Sin`, `IEEERemainder`)? Как std
  (`System.Math`) будет звать SharpLibm.
- `sqrt` через `sqrtsd`: `[Intrinsic] Math.Sqrt` в нашей std (ILC сам
  подставит инструкцию) или `Sse2.SqrtScalar` (в std нет `Intrinsics.X86`).
- Размер образа: ~27 тыс. строк и таблицы на функцию — проверить, сколько
  добавит ко всем приложениям (только используемые функции попадут в образ).
- Где гонять тесты: на хосте (.NET 10, быстро, для float — полный перебор) и
  выборку в QEMU (та же NoStdLib-среда).
