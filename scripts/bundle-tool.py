#!/usr/bin/env python3
"""Copies a command-line tool and every non-system library it links into an app's Tools folder,
rewriting load paths so the copy runs on Macs without Homebrew.

usage: bundle-tool.py <tool path> <Tools folder>
"""
import os
import shutil
import subprocess
import sys

SYSTEM = ("/usr/lib/", "/System/")


def linked(binary):
    out = subprocess.run(["otool", "-L", binary], capture_output=True, text=True, check=True).stdout
    return [line.strip().split(" (")[0] for line in out.splitlines()[1:] if line.strip()]


def rpaths(binary):
    out = subprocess.run(["otool", "-l", binary], capture_output=True, text=True, check=True).stdout.splitlines()
    found = []
    for index, line in enumerate(out):
        if line.strip() == "cmd LC_RPATH":
            for follow in out[index + 1:index + 4]:
                if follow.strip().startswith("path "):
                    found.append(follow.strip().split(" ")[1])
    return found


def resolve(reference, owner):
    """Turns @rpath/@loader_path references into real files, the way dyld would."""
    if reference.startswith("@rpath/"):
        name = reference[len("@rpath/"):]
        for base in rpaths(owner) + ["/opt/homebrew/lib", "/usr/local/lib"]:
            base = base.replace("@loader_path", os.path.dirname(owner)).replace("@executable_path", os.path.dirname(owner))
            candidate = os.path.join(base, name)
            if os.path.exists(candidate):
                return os.path.realpath(candidate)
        raise SystemExit(f"cannot resolve {reference} for {owner}")
    if reference.startswith("@loader_path/"):
        return os.path.realpath(os.path.join(os.path.dirname(owner), reference[len("@loader_path/"):]))
    return os.path.realpath(reference)


def main():
    tool, tools_dir = sys.argv[1], sys.argv[2]
    lib_dir = os.path.join(tools_dir, "lib")
    os.makedirs(lib_dir, exist_ok=True)
    target = os.path.join(tools_dir, os.path.basename(tool))
    shutil.copy2(os.path.realpath(tool), target)
    os.chmod(target, 0o755)

    queue = [(os.path.realpath(tool), target)]
    copied = {}
    while queue:
        original, copy = queue.pop()
        for reference in linked(original):
            if reference.startswith(SYSTEM) or reference == original:
                continue
            real = resolve(reference, original)
            if os.path.basename(real) == os.path.basename(original):
                continue
            name = os.path.basename(real)
            if name not in copied:
                destination = os.path.join(lib_dir, name)
                shutil.copy2(real, destination)
                os.chmod(destination, 0o755)
                subprocess.run(["install_name_tool", "-id", f"@loader_path/{name}", destination],
                               check=True, stderr=subprocess.DEVNULL)
                copied[name] = destination
                queue.append((real, destination))
            # The tool sits one level above lib/; libraries sit next to each other.
            new = f"@loader_path/lib/{name}" if copy == target else f"@loader_path/{name}"
            subprocess.run(["install_name_tool", "-change", reference, new, copy], check=True, stderr=subprocess.DEVNULL)

    for path in [target, *copied.values()]:
        subprocess.run(["codesign", "--force", "--sign", "-", path], check=True, capture_output=True)
    print(f"bundled {os.path.basename(tool)} with {len(copied)} libraries")


if __name__ == "__main__":
    main()
