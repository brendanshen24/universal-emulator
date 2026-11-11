# Mechanical clean-up of decoded XAML (XbfKit output) back to compilable source; layout and values are untouched.
#  - drop x:ConnectionId (compiler-internal; its x:Bind/event wiring is restored by hand in code-behind)
#  - BoolToValueConverter`1<T>.TrueValue/FalseValue attribute names -> TrueValue/FalseValue
#  - <NullExtension /> -> <x:Null />
import re, sys
src, dst = sys.argv[1], sys.argv[2]
x = open(src, encoding="utf-8").read()
x = re.sub(r'\n\s*x:ConnectionId="\d+"', "", x)
x = re.sub(r' x:ConnectionId="\d+"', "", x)
x = re.sub(r'(?:\w+:)?BoolToValueConverter_x0060_1_x003C_[\w.]+_x003E_\.(TrueValue|FalseValue)=', r"\1=", x)
x = x.replace("<NullExtension />", "<x:Null />")
open(dst, "w", encoding="utf-8").write(x)
