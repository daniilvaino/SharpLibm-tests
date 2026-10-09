# Тесты для libm, точность исходных библиотек и остальные находки

Дополнение к отчёту «Реализации libm на CSharp для NativeAOT»: четыре направления, оставшиеся без заметок, и отдельно — тесты для libm. Всё собрано чтением исходников и документов; **ничего не собиралось и не запускалось**.

Главное:

- **Тестов много, и самые ценные — не те, что лежат рядом с кандидатами.** Утверждение «корректное округление» проверяют только трудные случаи: файлы `.wc` из CORE-MATH (десятки тысяч и миллионы входов на функцию) плюс эталон MPFR. Собственные тесты CoreMathSharp их не используют: там 13 особых значений и около тысячи случайных битовых шаблонов на функцию.
- **Для портов «в пределах 1 ulp» готовый набор — musl libc-test**: у каждого вектора записана ошибка ожидаемого значения в ulp, так что измеряется именно ошибка в ulp, а не совпадение битов. Часть его данных под GPL и условиями Sun.
- **Уже на C# есть четыре набора**: тесты Go в go2cs, `MathFdlibmTests.cs` в Jint, векторы CoreMathSharp и фикстуры NetWasm.
- **Точность зависит от происхождения кода.** По сводке Циммермана (август 2026) линия fdlibm/msun даёт 0,83–1,02 ulp на `sin/cos/tan/exp/log`, но до 2,25 ulp на гиперболических и обратных к ним и **636 ulp на `pow`**; ядра ARM в musl — 0,51–0,56 ulp; CORE-MATH и LLVM-libc — 0,5 ulp; AMD LibM — 10,1 ulp на `sin/cos`.
- **Новая находка — CoreLib проекта NetWasm (MIT)**: порт fdlibm на C# с настоящей редукцией `__kernel_rem_pio2`, без кучи, статических конструкторов и нативного кода. Это лучший найденный источник `sin/cos/tan` классической линии.
- **В остальных C#-библиотеках полного libm для `double` нет**: либо обёртки над `System.Math`, либо приближения, либо другие числовые типы.
- **Неанглоязычные источники ничего не добавили.**

## Тесты для libm

### Наборы векторов и тестовые системы исходных библиотек

| Набор | Что внутри | Формат и объём | Лицензия |
|---|---|---|---|
| [musl libc-test, `src/math`](https://github.com/kraj/libc-test/blob/b7ec467969/src/math/sanity/sin.h) (зеркало, HEAD 2021-10-23) | Векторы почти для всех функций C99 (`double`, `float`, `long double`) в четырёх группах: `sanity`, `special`, `ucb`, `crlibm` | `T(RN, вход, ожидаемое, ошибка_ожидаемого_в_ulp, флаги)`, числа в hex-float. Строк: sanity 1 925; special 6 027 (к ближайшему — 4 834); ucb 13 047 (5 509); crlibm 62 507 (51 005) | Сам набор [MIT](https://github.com/kraj/libc-test/blob/b7ec467969/COPYRIGHT); `ucb/*.h` — условия Sun; `crlibm/*.h` — GPL v2 |
| [CORE-MATH, файлы `.wc`](https://github.com/mockingbirdnest/core-math/blob/3ce26eb72b/src/binary64/sin/sin.wc) (зеркало, HEAD 2026-08-16) | Только входы трудных случаев; ожидаемое считает MPFR. Режимы [`check.sh`](https://github.com/mockingbirdnest/core-math/blob/3ce26eb72b/check.sh): `--exhaustive`, `--worst`, `--special` | Hex-float по одному на строку, для двух аргументов `x,y`. binary64: sin 40 341, cos 38 507, tan 32 107, exp 1 110 898, log 134 944, pow 917 352, atan2 27 250, cbrt 105 543. binary32: powf 129 187, hypotf 6 883 | MIT |
| [Rust libm, `libm-test`](https://github.com/rust-lang/compiler-builtins/blob/9275fc9ebb/libm-test/src/precision.rs) | Не векторы, а генераторы: [граничные случаи](https://github.com/rust-lang/compiler-builtins/blob/9275fc9ebb/libm-test/src/generate/edge_cases.rs), случайные, равномерные, [список регрессий](https://github.com/rust-lang/compiler-builtins/blob/9275fc9ebb/libm-test/src/generate/case_list.rs). Сравнение с MPFR и с musl, полный перебор `f32` | Rust; допуски по функциям в `precision.rs` | MIT |
| [ARM Optimized Routines, `math/test`](https://github.com/ARM-software/optimized-routines/blob/59afb6fed4/math/test/testcases/directed/pow.tst) | Направленные векторы особых и граничных значений; спецификации случайных прогонов; программа [`ulp`](https://github.com/ARM-software/optimized-routines/blob/59afb6fed4/math/test/ulp.c) | `func=pow op1=… op2=… result=… errno=0`, слова в hex. 46 файлов, 2 697 векторов. Для `double` нет `sin/cos/tan` | MIT OR Apache-2.0 WITH LLVM-exception |
| [LLVM-libc, `libc/test/src/math`](https://github.com/llvm/llvm-project/blob/82e521a51d/libc/test/src/math/sin_test.cpp) | 280 файлов тестов, из них 100 сверяются с MPFR и 37 содержат списки трудных входов (`TrickyInputs`); [502 smoke-теста](https://github.com/llvm/llvm-project/blob/82e521a51d/libc/test/src/math/smoke/pow_test.cpp) особых значений без MPFR; `exhaustive/` — перебор `float` и [`sin.wc`](https://github.com/llvm/llvm-project/blob/82e521a51d/libc/test/src/math/exhaustive/sin.wc)/`cos.wc` | C++ | Apache-2.0 WITH LLVM-exception |
| [glibc, `auto-libm-test-in`](https://github.com/bminor/glibc/blob/04e750e75b/math/auto-libm-test-in) | Входы и [готовые ожидания](https://github.com/bminor/glibc/blob/04e750e75b/math/auto-libm-test-out-sin) для каждого режима округления и формата | `= sin tonearest binary64 0x… : 0x… : inexact-ok`. Входов 10 762 строки; 78 файлов ожиданий, 845 097 строк | LGPL-2.1+ |
| OpenJDK: [`StrictMath/*`](https://github.com/openjdk/jdk/blob/c5a311116a/test/jdk/java/lang/StrictMath/FdlibmTranslit.java), [`Math/WorstCaseTests.java`](https://github.com/openjdk/jdk/blob/c5a311116a/test/jdk/java/lang/Math/WorstCaseTests.java) | Точные ожидания для порта fdlibm; `FdlibmTranslit` — дословная транслитерация C как эталон для «идиоматичного» порта; трудные случаи Мюллера и Лефевра, худшие входы OpenLibm из сводки Циммермана, худшие случаи из Julia | Java; 18 файлов, 9 198 строк в `StrictMath` | GPLv2 |
| [Go, `src/math/all_test.go`](https://github.com/golang/go/blob/2557edd671/src/math/all_test.go) | Таблица входов `vf`, ожидания по функциям, таблицы особых случаев | Go; 4 055 строк. Допуски относительные: `close` 1e-14, `veryclose` 4e-16 | BSD-3-Clause |
| [FreeBSD, `lib/msun/tests`](https://github.com/freebsd/freebsd-src/blob/9a237074d3/lib/msun/tests/trig_test.c) | Особые значения и флаги исключений: тригонометрия, экспоненты, логарифмы, `fma`, `rem`, `next`, `lround`, `nearbyint`, `ilogb` | C (ATF) | BSD-2-Clause |
| [newlib, `libm/test/*_vec.c`](https://github.com/mirror/newlib-cygwin/blob/1b7c72fdcc/newlib/libm/test/sin_vec.c) | Таблицы «аргумент → результат» с требуемым числом совпадающих бит | Старшее и младшее слова в hex | лицензии newlib |
| [Berkeley TestFloat 3e](https://github.com/ucb-bar/berkeley-testfloat-3/blob/a9c849f1b0/README.md) | Генератор и проверка базовых операций IEEE: `sqrt`, fused multiply-add, IEEE-остаток, `roundToInt`, преобразования | `testfloat_gen` пишет случаи как hex-текст | BSD-3-Clause |
| [crlibm, `tests/*.testdata`](https://github.com/taschini/crlibm/blob/eb3063791a/tests/sin.testdata) | Трудные случаи для функций crlibm — первоисточник группы `crlibm` в libc-test | Старшая и младшая половины входа и выхода в hex | GPL |
| [AMD aocl-libm-ose, `gtests`](https://github.com/amd/aocl-libm-ose/blob/29fd054f38/gtests/exp/test_exp_data.h) | Данные конформности по функциям: вход, ожидаемое и ожидаемые флаги исключений | C-заголовки, битовые шаблоны | BSD-3-Clause |
| [CPython, `math_testcases.txt`](https://github.com/python/cpython/blob/main/Lib/test/mathdata/math_testcases.txt) | `erf`, `erfc`, `gamma`, `lgamma`, `expm1`, `log1p`, `log2` и π-функции (`sinpi`, `cospi`, `tanpi`, `acospi`, `asinpi`, `atanpi`, `atan2pi`) | `<id> <функция> <вход> -> <выход> <флаги>`; 2 254 строки | PSF |
| [test262, `staging/sm/Math/*-approx.js`](https://github.com/tc39/test262/blob/main/test/staging/sm/Math/expm1-approx.js) | Эталонные таблицы для `acosh`, `asinh`, `atanh`, `cosh`, `sinh`, `tanh`, `cbrt`, `expm1`, `log1p` с допуском в ulp | JavaScript | BSD |
| [V8, `ieee754-unittest.cc`](https://github.com/v8/v8/blob/main/test/unittests/base/ieee754-unittest.cc) | Точные ожидания для порта fdlibm в V8, 20 функций | C++; 405 строк | BSD |
| [dotnet/runtime, `Math.cs`](https://github.com/dotnet/runtime/blob/d21d8d491d/src/libraries/System.Runtime/tests/System.Runtime.Extensions.Tests/System/Math.cs) | То, что проверяет сам .NET: таблицы с допуском `CrossPlatformMachineEpsilon` | C# (xunit); 2 855 строк | MIT |

Три вещи из этой таблицы важно знать заранее.

- **Формат libc-test удобнее остальных для измерения ошибки.** Поле ошибки ожидаемого значения позволяет считать `ulperr = (got − want) / ulp(want) + dwant`; ошибка знака нуля считается бесконечной. Порог в самом наборе — 1,5 ulp с пометкой «TODO … should be 1.0» ([`mtest.c`](https://github.com/kraj/libc-test/blob/b7ec467969/src/common/mtest.c), [`mtest.h`](https://github.com/kraj/libc-test/blob/b7ec467969/src/common/mtest.h)). Группа `ucb` взята из netlib ucbtest, причём «some of the test outputs are changed to be correctly rounded» ([README](https://github.com/kraj/libc-test/blob/b7ec467969/src/math/ucb/README)).
- **Hex-float в C# не разбирается.** `double.Parse` не понимает `0x1.8p+1`, поэтому векторы libc-test, CORE-MATH и glibc нужно один раз перевести в битовые шаблоны. Наборы ARM и CoreMathSharp уже записаны битами.
- **Лицензии данных разные.** Группа `crlibm` в libc-test и исходные `.testdata` — GPL; `ucb` — условия Sun с запретом распространения за плату; glibc — LGPL; тесты OpenJDK — GPLv2 без Classpath exception. Перед включением таких данных в свой репозиторий условия стоит прочитать.

### Тесты, которые уже написаны на C#

| Где | Что проверяет | Замечание |
|---|---|---|
| [CoreMathSharp.Tests](https://github.com/andanteyk/CoreMathSharp/blob/77505b1bdb/CoreMathSharp.Tests/Binary64/Sin.cs) | Побитовое совпадение с C-версией CORE-MATH на [файлах векторов](https://github.com/andanteyk/CoreMathSharp/blob/77505b1bdb/CoreMathSharp.Tests/Binary64/sin.txt); сравнение с `System.Math` в пределах 1 ulp на 2×2^20 случайных входов | Векторы печатает [`c/sin.c`](https://github.com/andanteyk/CoreMathSharp/blob/77505b1bdb/c/sin.c): 13 особых значений и 1 024 случайных битовых шаблона (в `sin.txt` 1 037 строк). Файлы `.wc` в репозитории не упоминаются |
| [go2cs, `all_test.cs`](https://github.com/ritchiecarroll/go2cs/blob/master/src/core/math/all_test.cs) | Сконвертированные тесты Go `math` | Допуски Go: 1e-14 и 4e-16 относительно; завязаны на рантайм `golib` |
| [Jint, `MathFdlibmTests.cs`](https://github.com/sebastienros/jint/blob/b7e6ca0118/Jint.Tests/Runtime/MathFdlibmTests.cs) | Точные битовые шаблоны для `acosh`, `asinh`, `atanh`, `cbrt`, `expm1`, `log1p`; таблицы test262 | По комментарию в файле, ожидания сверены с 900-значной арифметикой, порт не отходит более чем на 1 ulp |
| [NetWasm, фикстура `numeric-managed-math-full-range`](https://github.com/zion-sati/NetWasm/blob/ffa2838604/tests/NetWasm.Compiler.Tests/Correctness/Fixtures/numeric-managed-math-full-range/full-range.case.json) | 1 004 входа по всему диапазону с эталонами, вычисленными на 2 048 и 2 304 битах; бюджет 4 ulp | Тесты под лицензией NetWasm Community, не MIT |

Про CoreMathSharp отсюда следует существенная оговорка к первому отчёту. Алгоритмы CORE-MATH устроены как быстрый путь и точный путь для трудных входов. Случайные битовые шаблоны попадают в точный путь редко, а трудные случаи из `.wc` автор не прогонял. В `Pow.cs` и `Atan2.cs` при этом есть ветка `throw new InvalidOperationException("Unexpected worst-case found…")` ([Pow.cs](https://github.com/andanteyk/CoreMathSharp/blob/77505b1bdb/CoreMathSharp/Binary64/Pow.cs)). Значит, верность переноса точного пути подтверждена слабее, чем быстрого, и именно её проверяет прогон `.wc` против MPFR или C-оригинала. Это мой вывод из состава тестов; запуском он не проверен.

### Какой набор к какой линии кода

Это моё сопоставление по происхождению алгоритмов, прогонами не проверенное.

- **Порт CORE-MATH** (CoreMathSharp): входы `.wc` с эталоном MPFR или C-оригинала; для `float` — полный перебор.
- **Порт fdlibm 5.3** (NetWasm): точные ожидания OpenJDK `StrictMath` и, с оговорками, V8 рассчитаны на код fdlibm, поэтому у верного порта должны совпадать побитово; исключение — `pow`, который в OpenJDK, по заголовку `FdLibm.java`, перенесён «with a fix to pow». У портов FreeBSD msun (Jint, части Cosmos gen3 и Go) возможны расхождения в последнем бите: msun с тех пор менялся.
- **Линия musl**: libc-test и допуски Rust libm.
- **Точные операции** (`fmod`, `remainder`, `fma`, `sqrt`, `floor` и подобные): Berkeley TestFloat, список регрессий Rust libm, группы `ucb` и `special` из libc-test. Допуск здесь — 0 ulp.
- **Особые значения для любой линии**: smoke-тесты LLVM-libc, тесты FreeBSD msun, направленные векторы ARM.

### Эталон для сравнения

- **MPFR из .NET.** [`Sdcb.Arithmetic.Gmp`](https://nuget.org/packages/Sdcb.Arithmetic.Gmp) и парный `Sdcb.Arithmetic.Mpfr` — современная привязка (Gmp 6.3.0 от 2025-03-17, net6.0+; обёртка MIT, нативные рантаймы LGPL для win-x64, win-x86, linux-x64). [`Math.Mpfr.Native.NET`](https://www.nuget.org/packages/Math.Mpfr.Native.NET) 2.0.0 — 2018 год, MPFR 4.0.0, .NET Framework.
- **Чистый C#.** `tk-yoshimura/MultiPrecision` и `DoubleDouble` (MIT), `PeterO.Numbers` (public domain; `EFloat` умеет `Exp`, `Log`, `Pow`, `Sqrt`, тригонометрии нет), `QuadrupleLib` (LGPL-3), `UltimateOrb` (MIT). Как эталон на обычном хосте они годятся, но MultiPrecision и DoubleDouble сами вызывают `Math.Log`, `double.Pow`, `double.Exp` — в сборку без libm их не взять.
- **Заранее посчитанные векторы** — то, как сделаны libc-test, glibc и CoreMathSharp: эталон считается один раз вне тестируемой среды.

### Что говорят сами источники о методике

- **Определение ошибки.** В сводке Циммермана это |y − z| в ulp точного значения z; для субнормальных — в единицах наименьшего субнормального; если NaN ровно один из двух — ошибка бесконечна.
- **Полный перебор `float`.** Для функций одного аргумента в binary32 перебираются все 2^32 входов: так делают CORE-MATH (`--exhaustive`), LLVM-libc (`exhaustive/`) и сама сводка, поэтому её цифры для `float` — точные максимумы, а для `double` — нижние границы.
- **Допуски по функциям.** Rust libm держит таблицу в `precision.rs`: 0 ulp для `fmod`, `fma`, `sqrt`, `floor`, `frexp`, `nextafter` и подобных; 1 ulp для `sin`, `cos`, `tan`, `exp`, `log`, `pow`; 2 — `asinh`, `atan2`, `atanh`, `sinh`, `tanh`; 4 — `acosh`, `erfc`, `lgamma`; 6 — `exp10`; 20 — `tgamma`; 8 000 000 — функции Бесселя.
- **Что считает нормой сам .NET.** Допуск в тестах `Math` — 2^-50 ≈ 8,88e-16 для ожиданий вида `0.xxx`, в десять раз больше для `x.xxx` и в десять раз меньше для `0.0xxx`, потому что машинный эпсилон 2^-52, по комментарию в тестах, «is slightly too accurate when writing tests meant to run against libm implementations for various platforms». По моей оценке это от нескольких до десятков ulp, так что любой порт в пределах 1–2 ulp эти тесты проходит с запасом.
- **Конкретные худшие входы.** Таблицы 2 и 4–19 сводки перечисляют худшие входы по библиотекам; программы поиска лежат на `gitlab.inria.fr/zimmerma/math_accuracy`, данные Лефевра — на `vinc17.net/research/testlibm/` (оба адреса я не открывал, они названы в статье и в заголовке `sin.wc`).

## Точность исходных libm

Источник — [Zimmermann и др., «Accuracy of Mathematical Functions in Single, Double, Double Extended, and Quadruple Precision»](https://members.loria.fr/PZimmermann/papers/accuracy.pdf), редакция августа 2026 года. Версии: GNU libc 2.44, Musl 1.2.6, OpenLibm 0.8.7, FreeBSD 15.1, Newlib 4.6.0, LLVM libc 22.1.8, AMD LibM 5.3, MSVC из Visual Studio 2026.

### `double`: наибольшая известная ошибка, ulp

| Функция | GNU libc | Musl | OpenLibm и FreeBSD | Newlib | LLVM libc | AMD LibM | MSVC |
|---|---|---|---|---|---|---|---|
| sin | 0.516 | 0.831 | 0.831 | 0.888 | 0.500 | **10.1** | 0.799 |
| cos | 0.516 | 0.834 | 0.834 | 0.887 | 0.500 | **10.1** | 0.897 |
| tan | 0.619 | 1.02 | 1.02 | 1.02 | 0.500 | 2.17 | 1.32 |
| asin | 0.516 | 0.981 | 0.981 | 0.981 | 0.500 | 1.06 | 0.960 |
| acos | 0.523 | 0.930 | 0.930 | 0.930 | 0.500 | 1.36 | 0.932 |
| atan | 0.523 | 0.861 | 0.861 | 0.861 | 1.00 | 0.863 | 0.863 |
| atan2 | 0.524 | 1.55 | 1.55 | 1.55 | 0.501 | 1.51 | 1.00 |
| sinh | **1.94e19** | 1.88 | 1.88 | 2.67 | — | 1.49 | 1.41 |
| cosh | 0.500 | 1.04 | 1.47 | 2.67 | — | 1.85 | 1.42 |
| tanh | 0.500 | 2.22 | 2.22 | 2.22 | — | 1.40 | 1.33 |
| asinh | 0.500 | 1.92 | 1.92 | 1.92 | — | 1.65 | 2.05 |
| acosh | 0.500 | **Inf** | 2.25 | 2.25 | — | 1.32 | 3.22 |
| atanh | 0.500 | 1.80 | 1.81 | 1.81 | — | 1.04 | 2.50 |
| exp | 0.511 | 0.511 | 0.949 | 0.949 | 0.500 | 1.01 | 1.50 |
| exp2 | 0.511 | 0.511 | 0.751 | 0.896 | 0.500 | 1.05 | 0.505 |
| exp10 | 0.513 | 4.14 | — | 0.896 | 0.500 | 1.02 | — |
| expm1 | 0.913 | 0.909 | 0.909 | 0.909 | 0.500 | 0.670 | 3.06 |
| log | 0.520 | 0.520 | 0.946 | 0.946 | 0.500 | 0.611 | 0.577 |
| log2 | 0.548 | 0.555 | 0.921 | 2.06 | 0.500 | 1.72 | 0.812 |
| log10 | 1.62 | 0.814 | 0.814 | 2.08 | 0.500 | 1.09 | 0.633 |
| log1p | 0.899 | 0.900 | 0.896 | 0.896 | 0.500 | 0.636 | 1.44 |
| pow | 0.523 | 0.525 | **636** | 0.895 | **Inf** | 1.00 | 0.505 |
| cbrt | 3.67 | 0.668 | 0.668 | 0.670 | 0.500 | 0.503 | 1.86 |
| hypot | 0.792 | 1.04 | 1.21 | 1.21 | 0.500 | 1.03 | 1.22 |
| erf | 0.500 | 1.02 | 1.02 | 1.02 | — | 1.00 | 3.32 |
| erfc | 0.500 | 3.72 | 4.08 | 4.08 | — | 4.49 | 6.04 |
| tgamma | 0.500 | 16.0 | 1.03e3 | 2.27e3 | — | — | 9.01e15 |
| lgamma | 0.500 | 4.45e15 | 4.45e15 | 4.45e15 | — | — | 5.10e13 |
| j0 | 4.51e14 | 4.51e14 | 4.51e14 | 9.01e15 | — | — | 2.42e33 |

Столбец «OpenLibm и FreeBSD» один, потому что их значения совпадают во всех однозначных строках (в восстановленных — по построению, см. ниже). Прочерк — функции в библиотеке нет. Для `double` это нижние границы наибольшей ошибки.

Оговорка о выравнивании: PDF читался через извлечение текста, пустые ячейки при этом выпали. Строки с 13 значениями однозначны; в строках `sin`, `cos`, `tan`, `hypot` пропуск стоит правее показанных столбцов и на них не влияет. В восьми строках (гиперболические, обратные гиперболические, `erf`, `erfc`) пустым я считаю столбец LLVM libc: только при таком выравнивании FreeBSD совпадает с OpenLibm, как во всех полных строках; статья пустой столбец не называет. В строках `exp10`, `tgamma`, `lgamma`, `j0` пропусков по три; я считаю пустыми OpenLibm, FreeBSD и MSVC для `exp10` и AMD, LLVM, ArmPL для остальных — это согласуется с фразами статьи о том, у каких библиотек `lgamma` и `tgamma` дают большие ошибки. Сырые строки обеих таблиц лежат в приложенном файле «01_точность_исходных_libm.md».

Аномалии, которые статья объясняет сама:

- **Musl `acosh` = Inf**: «For x = -0x1.34e729fd08086p+21, Musl acosh yields 0x1.075fa39542c7fp+3 instead of NaN» — конечное число вместо NaN для отрицательного аргумента.
- **LLVM libc `pow` = Inf**: −∞ вместо +∞ при x = 0x1.b4234939ab74ap+997, y = 0x1.59ccddd614389p+62.
- **`pow` = 636 ulp у OpenLibm и FreeBSD**: вход x = 0x1.000002c5e2e99p+0, y = 0x1.c9eee35374af6p+31; причину статья не называет.
- **AMD `sin`/`cos` = 10,1 ulp**: `cos` при 0x1.9eb7148f354d6p+20, `sin` при 0x1.9eb7148f354d6p+21; без объяснения.
- **GNU libc `sinh` = 1,94e19**: «a bug in the integration from the CORE-MATH code in GNU libc 2.44».

### `float`: наибольшая ошибка, ulp (полный перебор для функций одного аргумента)

| Функция | GNU libc | Musl | OpenLibm | FreeBSD | Newlib | LLVM libc | AMD LibM | MSVC |
|---|---|---|---|---|---|---|---|---|
| sinf | 0.561 | 0.501 | 0.501 | 0.501 | 1.37 | 0.500 | 0.530 | 0.530 |
| cosf | 0.561 | 0.501 | 0.501 | 0.501 | 2.91 | 0.500 | 0.729 | 0.530 |
| tanf | 0.500 | 0.800 | 0.800 | 0.800 | 3.48 | 0.500 | 0.509 | 0.502 |
| asinf | 0.500 | 0.743 | 0.743 | 0.672 | 0.926 | 0.500 | 0.781 | 0.846 |
| acosf | 0.500 | 0.918 | 0.918 | 0.895 | 0.899 | 0.500 | 0.897 | 0.653 |
| atanf | 0.500 | 0.853 | 0.853 | 0.853 | 0.853 | 0.500 | 0.501 | 0.501 |
| atan2f | 0.500 | 1.55 | 1.55 | 1.55 | 1.52 | 0.500 | 0.584 | 0.584 |
| expf | 0.502 | 0.502 | 0.911 | 0.911 | 0.911 | 0.500 | 0.501 | 0.501 |
| logf | 0.818 | 0.818 | 0.888 | 0.888 | 0.888 | 0.500 | 0.577 | 0.562 |
| powf | 0.817 | 0.817 | 0.970 | 0.970 | 1.00 | 0.501 | 1.56 | 1.00 |
| sinhf | 0.500 | 1.83 | 1.83 | 1.83 | 2.51 | 0.500 | 0.500 | 0.501 |
| tanhf | 0.500 | 2.19 | 2.19 | 2.19 | 2.19 | 0.500 | 0.500 | 1.24 |
| cbrtf | 0.500 | 0.500 | 0.500 | 0.500 | 3.56 | 0.500 | 0.548 | 1.83 |

### Что это значит для кандидатов на C#

Порт наследует алгоритм, поэтому таблица показывает ожидаемый потолок точности; сами порты никто не измерял, и ошибка переноса может только ухудшить цифры.

- **Линия fdlibm/msun** (столбец OpenLibm и FreeBSD): Cosmos gen3 (`exp`, `log`, `pow`, `cbrt`, `atan`, `asin`, `acos`), IKVM, Jint, NetWasm, RealSurvey, msun-части Go. Меньше 1,03 ulp на тригонометрии, `exp`, `log`, `cbrt`, `expm1`, `log1p`; 1,5–2,3 ulp на `atan2` и гиперболических. **Порты `e_pow.c` должны наследовать и худший случай в 636 ulp** — это Cosmos gen3 и IKVM; вход из статьи попадает в ветку `|y| > 2^31`, где логарифм считается укороченным рядом. В OpenJDK эта ветка сохранена ([FdLibm.java](https://github.com/openjdk/jdk/blob/c5a311116a/src/java.base/share/classes/java/lang/FdLibm.java)).
- **Линия musl**: части GeographicLib.NET и soft-float-библиотеки через Rust libm. Ядра `exp`, `exp2`, `log`, `log2`, `pow` взяты из ARM Optimized Routines и дают 0,51–0,56 ulp; автор ARM-кода заявляет для `pow` «Worst-case error: 0.54 ULP» ([pow.c](https://github.com/ARM-software/optimized-routines/blob/59afb6fed4/math/pow.c)). Остальное близко к msun; `acosh` содержит ошибку с отрицательными аргументами.
- **Линия AMD**: векторные `Sin/Cos/Exp/Log` в .NET и float-ядра Silk.NET. В `float` у AMD `sinf` — 0,530, `cosf` — 0,729, `expf` — 0,501, `logf` — 0,577, а `powf` — 1,56; в `double` у `sin/cos` известен вход с ошибкой 10,1 ulp. Перенесён ли тот же путь в `VectorMath.cs`, я не проверял.
- **Линия CORE-MATH**: CoreMathSharp. По построению 0,5 ulp. Строка `sinh` у GNU libc показывает цену ошибки переноса: алгоритм корректно округлённый, а интеграция дала 1,94e19 ulp.
- **Столбцы GNU libc и MSVC** — ориентир для штатного .NET на Linux и Windows, где `Math.*` уходит в системную математику. У MSVC `exp` — 1,50 ulp, `expm1` — 3,06, `acosh` — 3,22; планка «не хуже штатного .NET на Windows» невысока.

## Математика в других C#-библиотеках

Полного libm для `double` без `System.Math` среди них нет. По группам:

| Группа | Библиотеки | Что реально есть | Лицензия | Вердикт |
|---|---|---|---|---|
| Управляемые ядра для `float` | [Silk.NET.Maths](https://github.com/dotnet/Silk.NET/blob/2e8eaf6ba4/src/Maths/Silk.NET.Maths/Scalar.Sin.cs) | `sinf` — порт `sinf.asm` из AMD win-libm; [`expf`](https://github.com/dotnet/Silk.NET/blob/2e8eaf6ba4/src/Maths/Silk.NET.Maths/Scalar.Exp.cs) и [`logf`](https://github.com/dotnet/Silk.NET/blob/2e8eaf6ba4/src/Maths/Silk.NET.Maths/Scalar.Log.cs) — из aocl-libm-ose; [`powf`](https://github.com/dotnet/Silk.NET/blob/2e8eaf6ba4/src/Maths/Silk.NET.Maths/Scalar.Pow.cs) — `Exp(y * Log(x))`. Для `double` — `Math.Sin`, `Math.Exp`, `Math.Log`. [Отдельно](https://github.com/dotnet/Silk.NET/blob/2e8eaf6ba4/src/Maths/Silk.NET.Maths/Scalar.MathFPort.MissingMethods.cs) `atanh`, `log1p`, `acosh`, `asinh`, `cbrt` для `double` из Go (FreeBSD msun) | MIT и уведомления AMD | Донор float-ядер линии AMD |
| SIMD-приближения | [LitMath](https://github.com/matthewkolbe/LitMath/blob/d63b1c1df5/README.md), MathSharp, MaxMath | LitMath: `Exp`, `Log`, `Sin`, `Cos`, `Tan`, `ATan`, `ERF` на AVX2/AVX-512, ряды Тейлора; заявлено 1e-15 для `Sin/Log/Cos`, 1e-10 для `ATan`. Последний коммит 2024-03-28 | Apache-2.0 | Только x86, не libm-класс |
| Детерминированные приближения | [Jitter2 `StableMath`](https://github.com/notgiven688/jitterphysics2/blob/aa6c705a92/src/Jitter2/LinearMath/StableMath.cs), [BepuUtilities `MathHelper`](https://github.com/bepu/bepuphysics2/blob/c230dd1178/BepuUtilities/MathHelper.cs), [`DeMath`](https://github.com/facioquo/stock-indicators-dotnet/blob/538943d580/src/Common/Math/DeMath.cs) | StableMath: редукция «intentionally the "naive" reducer», не Payne–Hanek. Bepu: ошибка `cos` около 8e-7. DeMath: свои `Exp`, `Log`, `Log10`, `Atan`, `Atan2`, `Atanh` | MIT, Apache-2.0, Apache-2.0 | Побитовая воспроизводимость ценой точности |
| ILGPU | [`XMath` в 1.5.3](https://github.com/m4rs-mt/ILGPU/blob/v1.5.3/Src/ILGPU.Algorithms/XMath/Cordic.tt), [текущий `XMath`](https://github.com/m4rs-mt/ILGPU/blob/9b843b5f36/Src/ILGPU/XMath/Trig.cs) | В 1.5.3 — CORDIC-шаблоны (`Sin`, `Cos`, `Atan`, `Log`, `Exp`) и [`PTXMath`](https://github.com/m4rs-mt/ILGPU/blob/v1.5.3/Src/ILGPU.Algorithms/PTX/PTXMath.cs). В текущей ветке `XMath.Sin` — это `Math.Sin` | University of Illinois/NCSA | CORDIC не libm-класс; сейчас обёртка |
| Обёртки над `System.Math` | [Unity.Mathematics](https://github.com/Unity-Technologies/Unity.Mathematics/blob/f53664de3d/src/Unity.Mathematics/math.cs), [Cesium.Runtime](https://github.com/ForNeVeR/Cesium/blob/840f330f3c/Cesium.Runtime/MathFunctions.cs), WACS, NiL.JS, Jurassic, MoonSharp, Peachpie, JavApi | Прямые вызовы `System.Math` | разные | Не подходят |
| Soft-float, binary32 | [Kimbatt/soft-float-starter-pack](https://github.com/Kimbatt/soft-float-starter-pack/blob/eb09e0e3e9/SoftFloat/libm/Trigonometry.cs), [CodesInChaos/SoftFloat](https://github.com/CodesInChaos/SoftFloat/blob/201b3efe11/SoftFloatTest/SoftFloat.cs), [nilpunch/soft-math](https://github.com/nilpunch/soft-math/blob/df05c66fca/README.md) | Kimbatt: тип `sfloat` и libm из Rust libm — `sinf`, `cosf`, `tanf`, `asinf`, `acosf`, `atan2f`, `hypotf`, `expf`, `logf`, `log2f`, `powf`, `sqrtf`, `fmodf`, `remainderf`, `remquof`, `floorf`, `ceilf`, `truncf`, `roundf`. Только целочисленная арифметика | MIT | Единственные найденные управляемые `fmodf` и `remquof`, но над типом `sfloat`; только `float`, 2021 год |
| Fixed-point | [FixPointCS](https://github.com/XMunkki/FixPointCS/blob/a852f05b42/README.md), [FixedMath.Net](https://github.com/asik/FixedMath.Net/blob/b2adac7713/src/Fix64.cs), FixedMathSharp, FixedPoint-Sharp, Unity.Mathematics.FixedPoint | 32.32 и 16.16; у FixPointCS варианты на 24, 16 и 10 бит точности | FixPointCS и FixedMathSharp — MIT, FixedMath.Net — Apache-2.0 | Другой числовой тип |
| Расширенная точность | [DoubleDouble](https://github.com/tk-yoshimura/DoubleDouble/blob/4ba19e70ba/README.md), MultiPrecision, [QuadrupleLib](https://github.com/IsaMorphic/QuadrupleLib/blob/339fffdad7/README.md), [UltimateOrb](https://github.com/LEI-Hongfaan/UltimateOrb.Core/blob/77aaf7758d/UltimateOrb.Core/Quadruple.cs), NetQD, PeterO.Numbers | Элементарные и специальные функции для double-double, binary128, произвольной точности | MIT; QuadrupleLib — LGPL-3; PeterO — public domain | Эталон для тестов, не замена libm |
| Специальные функции | [MathXK](https://github.com/rromeo/MathXK/blob/3eefa7d0d2/README.md), [Accord.Math](https://github.com/accord-net/framework/blob/1ab0cc0ba5/Sources/Accord.Math/Special.cs), Math.NET Numerics | MathXK — порт Boost Math Toolkit (2019); Accord — Cephes 2.8 (проект в архиве, последний коммит 2020) | Boost, LGPL-2.1, MIT | Доноры `erf`/`gamma`, элементарные идут в `System.Math` |

## Длинный хвост и неанглоязычные источники

### NetWasm: порт fdlibm в MIT-лицензированном CoreLib

[`zion-sati/NetWasm`](https://github.com/zion-sati/NetWasm) — независимый компилятор C# в WebAssembly со своим CoreLib (61 звезда, 145 коммитов, HEAD 2026-10-06). В первом отчёте его не было. Его `System.Math` — управляемый порт Sun fdlibm:

- **Верные порты**: `Sin`, `Cos`, `Tan` с полными [`__kernel_rem_pio2`](https://github.com/zion-sati/NetWasm/blob/ffa2838604/src/NetWasm.CoreLib/Math.FdKernelRemPio2.cs) и `__ieee754_rem_pio2`, `Exp`, `Log`, `Sinh`, `Tanh`, `Acosh`, ядра `expm1` и `log1p`.
- **Упрощено** в [`Math.cs`](https://github.com/zion-sati/NetWasm/blob/ffa2838604/src/NetWasm.CoreLib/Math.cs): `Pow` — `Exp(y * Log(x))` и использует `%`; `Cosh` — `0.5 * (e^x + 1/e^x)`; `Asin` — через `Atan2`; `Cbrt` — `Exp(Log(x) / 3)` с пятью шагами Ньютона; `Log10` и `Log2` — деление `Log`; float-версии — приведения над `double`.
- **Как сделано** (по [`Math.fdlibm.md`](https://github.com/zion-sati/NetWasm/blob/ffa2838604/src/NetWasm.CoreLib/Math.fdlibm.md)): «Constant tables are method-local read-only spans and bounded scratch buffers use stack allocation. They have no shared Math type initializer, heap allocation or native dependency». Для доступа к битам используется только `BitConverter`; таблицы записаны как локальные `ReadOnlySpan` с collection expressions.
- **Тесты**: 402 и 1 004 входа с бюджетом 4 ulp; авторы оговаривают, что это «bounded regression evidence, not a claim that every transcendental result is correctly rounded».
- **Лицензия**: `src/NetWasm.CoreLib/**` — MIT ([LICENSE-MAP.md](https://github.com/zion-sati/NetWasm/blob/ffa2838604/LICENSE-MAP.md)); компилятор и тесты — NetWasm Community License.

Относительно первого отчёта это меняет одно: для `sin/cos/tan` классической линии fdlibm появился источник под MIT, написанный сразу под CoreLib без кучи. Раньше в этой роли были IKVM (GPLv2 с Classpath exception, массивы в куче) и RealSurvey (заброшен в 2019 году).

### Остальное

- [`bleroy/netduinohelpers`, `Math/Trigo.cs`](https://github.com/bleroy/netduinohelpers/blob/6e806d8ea6/Math/Trigo.cs) — потомок exMath Эльзе Кула (2009) для .NET Micro Framework, переведённый на `float`; исторический пример приближений, файл объявлен public domain.
- **Русскоязычные, китайские и японские источники** (Habr, CSDN, cnblogs, Qiita, Zenn и общий поиск) дали только обзорные статьи о NativeAOT. Ни одной реализации libm на C# и ни одного отчёта об опыте замены `System.Math` в них не нашлось. Покрытие — восемь поисковых запросов на трёх языках, без обхода Gitee и форумов вручную.
- Попутно найден [порт CORE-MATH на Zig](https://zff.dev/castholm/core-math-ports) — ещё один образец переноса тех же алгоритмов; страницу я не открывал.

## Ловушки C# при переносе

Только то, что подтверждено первичным источником.

- **Режим округления и исключения.** ECMA-335 §I.12.1.3: режим «shall be set by the CLI to "round to the nearest number"», менять его нечем, и реализации «can rely on it having been set as part of their initialization». Исключений FP CLI не порождает. Следствия: в процессе без рантайма начальное состояние FPU — забота проекта; идиомы C вроде `huge * huge` ради флага переполнения не нужны. OpenJDK при переносе заменил их прямым `return INFINITY` ([FdLibm.java](https://github.com/openjdk/jdk/blob/c5a311116a/src/java.base/share/classes/java/lang/FdLibm.java)).
- **Избыточная точность промежуточных значений.** Тот же параграф разрешает внутреннее представление «with additional range and/or precision» вне мест хранения. Автор CoreMathSharp прямо оговаривает 32-битную среду с x87: «correct results may not be obtained».
- **Приведение `double` к целому.** С .NET 9 на x86 и x64 оно насыщающее: вне диапазона — `MinValue`/`MaxValue`, NaN → 0; раньше для `int` и `long` возвращалось `MinValue` ([документ о breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/jit/9.0/fp-to-integer)). Код, который приводит к целому значение вне его диапазона (в C это неопределённое поведение), даёт разные результаты на .NET 8 и .NET 9+.
- **Переполнение целых и неявные приведения.** Rust libm выносит это в «Porting Reminders»: «C assumes wrapping operations everywhere» и «Note C implicit casts, especially integer promotion» ([CONTRIBUTING.md](https://github.com/rust-lang/compiler-builtins/blob/9275fc9ebb/CONTRIBUTING.md)). Для C# первое существенно при включённом `checked`.
- **Hex-float-литералов нет** ни в Rust, ни в C#. Rust решает это макросами `hf64!`, CoreMathSharp — десятичной записью констант.
- **Доступ к битам.** И OpenJDK, и NetWasm заменили наложение `double` на пару `int` стандартными функциями преобразования битов (`doubleToRawLongBits`, `BitConverter.DoubleToUInt64Bits`).
- Какие вызовы `Math.*` и оператор `%` превращаются в ссылки на libm — разобрано в первом отчёте.

## Ограничения

- **Ничего не собиралось и не запускалось.** Все утверждения о точности — либо цифры из статьи для C-оригиналов, либо заявления авторов портов, либо мои выводы из чтения кода.
- **Таблица точности** получена извлечением текста из PDF; выравнивание двенадцати строк с пропусками восстановлено мной (см. оговорку под таблицей). Перед тем как на неё опираться, эти строки стоит сверить с оригиналом.
- **Страница HAL** со списком редакций статьи оказалась закрыта анти-бот-защитой; дата «август 2026» и версии библиотек взяты из самого PDF.
- **Не открывал**: `vinc17.net/research/testlibm/`, `gitlab.inria.fr/zimmerma/math_accuracy`, страницу Zig-порта.
- **Библиотеки** просмотрены по ключевым файлам, не целиком; у репозиториев, склонированных частично, я видел только файлы с математикой.
- **Неанглоязычный поиск** был поверхностным: отсутствие находок означает «не нашлось этими запросами», а не «не существует».
