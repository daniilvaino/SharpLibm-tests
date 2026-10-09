"""smoke.c -> smoke_dyn.c: every libm function bound from SharpLibm's library
at run time — GetProcAddress on sharplibm.dll, or dlsym on ./sharplibm.so with
`gen_dyn.py linux` — so none can come from the toolchain's own libm."""
import re
import sys

linux = len(sys.argv) > 1 and sys.argv[1] == "linux"

src = open("smoke.c", encoding="utf-8").read()
start = src.index("double sin(double)")
end = src.index("static uint64_t b(")
names, out = [], []
for line in src[start:end].strip().splitlines():
    ret = line.split()[0]
    for m in re.finditer(r"(\w+)\(([^)]*)\)", line):
        name, args = m.group(1), m.group(2)
        names.append(name)
        out.append(f"static {ret} (*p_{name})({args});\n#define {name} p_{name}")
header = "#include <dlfcn.h>\n" if linux else "#include <windows.h>\n"
src = src[:start] + header + "\n".join(out) + "\n\n" + src[end:]

nl = "\\n"
lib = "./sharplibm.so" if linux else "sharplibm.dll"
if linux:
    bind = f'    void *h = dlopen("{lib}", RTLD_NOW | RTLD_LOCAL);\n'
else:
    bind = f'    HMODULE h = LoadLibraryA("{lib}");\n'
bind += f'    if (!h) {{ printf("no {lib}' + nl + '"); return 2; }\n'
for n in names:
    get = f'dlsym(h, "{n}")' if linux else f'(void *)GetProcAddress(h, "{n}")'
    bind += (f'    *(void **)&p_{n} = {get};'
             f' if (!p_{n}) {{ printf("missing export {n}' + nl + '"); return 2; }\n')
src = src.replace("int main(void) {\n", "int main(void) {\n" + bind, 1)
open("smoke_dyn.c", "w", encoding="utf-8").write(src)
print(len(names), "functions bound")
