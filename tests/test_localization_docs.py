import json
import pathlib
import re
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class LocalizationAndDocsTests(unittest.TestCase):
    def test_explicit_localized_messages_have_catalog_entries(self):
        entries = json.loads((ROOT / "src" / "Translations.json").read_text(encoding="utf-8"))
        for source in (ROOT / "src").glob("*.cs"):
            for key in re.findall(r'L\.Text\("([^"\n]+)"', source.read_text(encoding="utf-8")):
                if re.search(r"[\u4e00-\u9fff]", key):
                    self.assertIn(key, entries, (source.name, key))

    def test_catalog_complete_and_format_arguments_preserved(self):
        entries = json.loads((ROOT / "src" / "Translations.json").read_text(encoding="utf-8"))
        self.assertGreater(len(entries), 80)
        for source, translations in entries.items():
            self.assertEqual(len(translations), 5, source)
            args = set(re.findall(r"\{\d+\}", source))
            for translated in translations:
                self.assertTrue(translated.strip(), source)
                self.assertEqual(set(re.findall(r"\{\d+\}", translated)), args, source)

    def test_embedded_english_guide_matches_standalone(self):
        chinese = (ROOT / "README.md").read_text(encoding="utf-8")
        english = (ROOT / "README.en.md").read_text(encoding="utf-8").strip()
        embedded = chinese.split("<!-- ENGLISH_GUIDE_START -->", 1)[1].split("<!-- ENGLISH_GUIDE_END -->", 1)[0].strip()
        self.assertEqual(embedded, english)
        for guide in (chinese, english):
            self.assertIn("https://github.com/misiektoja/steam_monitor", guide)
            self.assertIn("https://github.com/misiektoja", guide)

    def test_local_markdown_links_resolve(self):
        for name in ("README.md", "README.en.md"):
            text = (ROOT / name).read_text(encoding="utf-8")
            targets = re.findall(r"\]\(([^)]+)\)", text) + re.findall(r'href="([^"]+)"', text)
            for target in targets:
                if target.startswith(("https://", "http://", "#")):
                    continue
                self.assertTrue((ROOT / target.split("#")[0]).exists(), (name, target))


if __name__ == "__main__":
    unittest.main()
