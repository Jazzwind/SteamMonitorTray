import importlib.util
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "scripts" / (name + ".py"))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


inspect_config = module("InspectConfig")
privacy = module("ScanPrivacy")


class ConfigTests(unittest.TestCase):
    def setUp(self):
        base = ROOT / "artifacts" / "unit-tests"
        base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=base)
        self.addCleanup(self.temp.cleanup)
        self.directory = pathlib.Path(self.temp.name)

    def inspect(self, extra=""):
        config = self.directory / "profile.conf"
        config.write_text("TARGET_STEAM_ID = 'fixture'\nFILE_SUFFIX = 'fixture'\nSTEAM_STATUS_FILE = 'state.json'\n" + extra, encoding="utf-8")
        return inspect_config.inspect(config, self.directory)

    def test_log_with_extension_is_not_resuffixed(self):
        result = self.inspect("ST_LOGFILE = 'output.log'\n")
        self.assertIn(str((self.directory / "output.log").resolve()).lower(), result["outputs"])

    def test_static_inspection_never_executes_config(self):
        with self.assertRaises(ValueError):
            self.inspect("import pathlib\npathlib.Path('not-created').touch()\n")
        self.assertFalse((self.directory / "not-created").exists())

    def test_dynamic_output_and_conditional_assignments_rejected(self):
        with self.assertRaises(ValueError):
            self.inspect("CSV_FILE = str('dynamic.csv')\n")
        with self.assertRaises(ValueError):
            self.inspect("if True:\n    CSV_FILE = 'conditional.csv'\n")

    def test_same_file_used_for_different_outputs_rejected(self):
        with self.assertRaises(ValueError):
            self.inspect("CSV_FILE = 'state.json'\n")

    def test_game_snapshot_included(self):
        target = "7" * 17
        result = self.inspect("TARGET_STEAM_ID = '" + target + "'\nGAMES_LIBRARY_CHECK = True\n")
        self.assertTrue(any(path.endswith("_games.json") for path in result["outputs"]))

    def test_credentials_are_not_in_inspection_result(self):
        result = self.inspect("STEAM_API_KEY = '" + "a" * 32 + "'\nWEBHOOK_URL = 'private-placeholder'\n")
        self.assertNotIn("a" * 32, str(result))
        self.assertNotIn("private-placeholder", str(result))


class PrivacyTests(unittest.TestCase):
    def setUp(self):
        privacy.failures.clear()

    def test_scan_catches_identifiers_and_utf16_credentials(self):
        privacy.check("fixture", ("7" * 17).encode())
        privacy.check("fixture", ("a" * 32).encode("utf-16-le"))
        self.assertEqual({rule for _, rule in privacy.failures}, {"steam-identifier", "api-key-shape"})

    def test_private_file_names(self):
        for name in (".env", "local/.env.backup", "profile.conf", "settings.json", "history.csv"):
            self.assertTrue(privacy.private_name(name))
        self.assertFalse(privacy.private_name("scripts/InspectConfig.py"))


if __name__ == "__main__":
    unittest.main()
