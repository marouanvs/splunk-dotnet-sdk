#!/usr/bin/env python3
"""Validate the repo-local Codex skill shape without external dependencies."""

from __future__ import annotations

import re
import sys
from pathlib import Path


def fail(message: str) -> int:
    print(f"ERROR: {message}", file=sys.stderr)
    return 1


def parse_frontmatter(text: str) -> dict[str, str] | None:
    match = re.match(r"^---\n(?P<body>.*?)\n---\n", text, re.DOTALL)
    if not match:
        return None

    fields: dict[str, str] = {}
    for line in match.group("body").splitlines():
        if not line.strip() or line.strip().startswith("#"):
            continue

        if ":" not in line:
            return None

        key, value = line.split(":", 1)
        fields[key.strip()] = value.strip().strip("\"'")

    return fields


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        return fail("usage: validate-skill.py <skill-directory>")

    skill_dir = Path(argv[1])
    skill_file = skill_dir / "SKILL.md"
    openai_yaml = skill_dir / "agents" / "openai.yaml"

    if not skill_dir.is_dir():
        return fail(f"skill directory does not exist: {skill_dir}")

    if not skill_file.is_file():
        return fail(f"missing SKILL.md: {skill_file}")

    text = skill_file.read_text(encoding="utf-8")
    frontmatter = parse_frontmatter(text)
    if frontmatter is None:
        return fail("SKILL.md must start with YAML frontmatter delimited by ---")

    name = frontmatter.get("name")
    description = frontmatter.get("description")

    if not name:
        return fail("SKILL.md frontmatter must include name")

    if not re.fullmatch(r"[a-z0-9-]{1,63}", name):
        return fail("skill name must be 1-63 chars of lowercase letters, digits, or hyphens")

    if name != skill_dir.name:
        return fail(f"skill name '{name}' must match directory name '{skill_dir.name}'")

    if not description or "TODO" in description:
        return fail("SKILL.md frontmatter must include a completed description")

    if "TODO" in text:
        return fail("SKILL.md still contains TODO markers")

    if not openai_yaml.is_file():
        return fail(f"missing agents/openai.yaml: {openai_yaml}")

    yaml_text = openai_yaml.read_text(encoding="utf-8")
    for required in ("display_name:", "short_description:", "default_prompt:"):
        if required not in yaml_text:
            return fail(f"agents/openai.yaml missing {required}")

    if f"${name}" not in yaml_text:
        return fail(f"agents/openai.yaml default_prompt should mention ${name}")

    print("Skill is valid!")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
