"""Ad-hoc structural check for SettingsWindow.xaml — Task 10.

Same four checks as scripts/verify-xaml-surface.py but pointing at
the new settings window. Run after every edit to SettingsWindow.xaml
or SettingsViewModel.cs to catch typos `dotnet build` cannot.

Usage: python verify-settings-xaml-surface.py
"""

import os
import re
import sys

ROOT = r"C:\Users\Herlandro Ando\Documents\Ando\sites_win\TrackDot"
XAML_FILE = "SettingsWindow.xaml"
APP_FILE = "App.xaml"
VM_FILE = r"ViewModels\SettingsViewModel.cs"
CODE_BEHIND_FILE = "SettingsWindow.xaml.cs"


def read(path):
    with open(os.path.join(ROOT, path), encoding="utf-8-sig") as f:
        return f.read()


xaml = read(XAML_FILE)
app = read(APP_FILE)
vm = read(VM_FILE)
code_behind = read(CODE_BEHIND_FILE)

failures = []

# 1) StaticResource keys — defined in either App.xaml or the
# XAML file's own <Window.Resources> block (local styles).
defined_keys = set(re.findall(r'x:Key="([^"]+)"', app))
defined_keys |= set(re.findall(r'x:Key="([^"]+)"', xaml))
used_keys = set(re.findall(r'\{StaticResource\s+([A-Za-z_][\w]*)\}', xaml))
missing = used_keys - defined_keys
if missing:
    failures.append(f"{XAML_FILE} references missing StaticResource keys: {sorted(missing)}")

# 2) Binding name -> VM public property
bindings = set(re.findall(r'\{Binding\s+([A-Za-z_][\w]*)', xaml))
declared = set(re.findall(
    r'public\s+[\w<>\?\s,.]+?\s([A-Za-z_][\w]*)\s*(?:=>|\{)', vm))
missing = bindings - declared
if missing:
    failures.append(f"{XAML_FILE} binds to missing VM properties: {sorted(missing)}")

# 3) Command targets (SettingsWindow has none, but check defensively)
cmd_bindings = set(re.findall(r'Command="\{Binding\s+([A-Za-z_][\w]*)\}"', xaml))
missing = cmd_bindings - declared
if missing:
    failures.append(f"Command targets are not declared VM members: {sorted(missing)}")

# 4) Code-behind handlers
handler_refs = set(re.findall(r'="([A-Za-z_][\w]*_[A-Za-z][\w]*)"', xaml))
declared_methods = set(re.findall(r'private\s+void\s+([A-Za-z_][\w]*)\s*\(', code_behind))
missing = handler_refs - declared_methods
if missing:
    failures.append(f"XAML references undeclared code-behind methods: {sorted(missing)}")

if failures:
    print("FAIL")
    for f in failures:
        print(f"  - {f}")
    sys.exit(1)

print(
    f"PASS  static={len(used_keys)} bindings={len(bindings)} "
    f"commands={len(cmd_bindings)} handlers={len(handler_refs)}"
)