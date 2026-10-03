#!/usr/bin/env python3
"""Syntax/type check of addon/shaders/MCPassthrough.fx without ReShade.

ReShade's effect compiler is not available outside Windows, so this rewrites the effect into plain HLSL (textures + samplers
become Texture2D/SamplerState pairs, annotations and the technique are dropped) and lets glslang's HLSL front end compile every
pixel shader. It proves our own code parses and type-checks (undeclared names, wrong swizzles, bad types); it cannot prove
ReShade accepts every FX-specific construct. Usage: fxcheck.py [path-to-fx]   (needs glslangValidator: apt install glslang-tools)
"""
import re, subprocess, sys, tempfile, os

path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "../../addon/shaders/MCPassthrough.fx")
src = open(path).read()

SHIM = r'''
#define BUFFER_WIDTH 2560
#define BUFFER_HEIGHT 1080
#define BUFFER_RCP_WIDTH (1.0 / 2560.0)
#define BUFFER_RCP_HEIGHT (1.0 / 1080.0)
#define BUFFER_PIXEL_SIZE float2(BUFFER_RCP_WIDTH, BUFFER_RCP_HEIGHT)
#define tex2D(s, uv) s##_tex.Sample(s##_ss, uv)
#define tex2Dlod(s, v) s##_tex.SampleLevel(s##_ss, (v).xy, (v).w)
#define tex2Dfetch(s, c) s##_tex.Load(int3((c).x, (c).y, 0))
Texture2D ReShade_BackBuffer_tex; SamplerState ReShade_BackBuffer_ss;
Texture2D ReShade_DepthBuffer_tex; SamplerState ReShade_DepthBuffer_ss;
'''

s = src
s = s.replace('#include "ReShade.fxh"', '')
s = s.replace('ReShade::BackBuffer', 'ReShade_BackBuffer').replace('ReShade::DepthBuffer', 'ReShade_DepthBuffer')
# technique block (last thing in the file)
s = s[:s.index('technique ')]
# texture declarations
s = re.sub(r'texture\s+(\w+)\s*(:\s*\w+)?\s*;', r'Texture2D \1;', s)
s = re.sub(r'texture\s+(\w+)\s*\{[^}]*\}\s*;', r'Texture2D \1;', s)
# samplers: sampler sX { Texture = X; ... };
s = re.sub(r'sampler\s+(\w+)\s*\{\s*Texture\s*=\s*(\w+)\s*;[^}]*\}\s*;', r'Texture2D \1_tex; SamplerState \1_ss;', s)
# uniform annotations < ... > before = or ;
s = re.sub(r'(uniform\s+\w+\s+\w+)\s*<.*?>\s*(=|;)', r'\1 \2', s, flags=re.S)
# the technique-less file still has comments with ReShade words: fine
hlsl = SHIM + s

entries = re.findall(r'^(?:void|float4|float3|float2|float)\s+(PS_\w+)\s*\(', s, flags=re.M)
if not entries:
    print("no pixel shaders found"); sys.exit(2)
tmp = tempfile.mkdtemp()
p = os.path.join(tmp, "fx.hlsl")
open(p, "w").write(hlsl)
failed = 0
for e in entries:
    r = subprocess.run(["glslangValidator", "-D", "-V", "-S", "frag", "-e", e, "--sep", "ps", "-o", os.path.join(tmp, e + ".spv"), p], capture_output=True, text=True)
    out = (r.stdout + r.stderr).strip()
    if r.returncode != 0:
        failed += 1
        print("FAIL", e)
        print(out)
    else:
        print("ok  ", e)
if failed:
    print("\n(generated HLSL kept at %s)" % p)
    sys.exit(1)
