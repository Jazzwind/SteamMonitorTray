"""Keep README.md's optional same-page English guide equal to README.en.md."""
import argparse
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
START = "<!-- ENGLISH_GUIDE_START -->"
END = "<!-- ENGLISH_GUIDE_END -->"


def sync(check=False):
    readme = ROOT / "README.md"
    text = readme.read_text(encoding="utf-8")
    if text.count(START) != 1 or text.count(END) != 1:
        raise ValueError("Expected one English guide marker pair.")
    before, rest = text.split(START, 1)
    previous, after = rest.split(END, 1)
    english = (ROOT / "README.en.md").read_text(encoding="utf-8").strip()
    if check:
        return previous.strip() == english
    readme.write_text(before + START + "\n" + english + "\n" + END + after, encoding="utf-8")
    return True


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    result = sync(args.check)
    print("PASS English README sync" if result else "FAIL English README is out of sync")
    sys.exit(not result)
