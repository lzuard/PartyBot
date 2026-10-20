import importlib.util
from pathlib import Path
import sqlite3
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("backup", Path(__file__).with_name("sqlite_backup.py"))
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class BackupTests(unittest.TestCase):
    def test_online_wal_backup_restore_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, copy, restored = [root / name for name in ("source.db", "copy.db", "restored.db")]
            live = sqlite3.connect(source)
            try:
                live.execute("PRAGMA journal_mode=WAL")
                live.execute("CREATE TABLE data (value TEXT)")
                live.execute("INSERT INTO data VALUES ('committed in WAL')")
                live.commit()
                backup.copy_database(source, copy)
                live.execute("INSERT INTO data VALUES ('after backup')")
                live.commit()
                backup.copy_database(copy, restored)
                with sqlite3.connect(restored) as result:
                    self.assertEqual([("committed in WAL",)], result.execute("SELECT * FROM data").fetchall())
                with self.assertRaises(FileExistsError):
                    backup.copy_database(source, restored)
                backup.verify(restored)
            finally:
                live.close()

    def test_bad_or_missing_source_never_creates_destination(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            destination = root / "new.db"
            with self.assertRaises(sqlite3.Error):
                backup.copy_database(root / "missing.db", destination)
            self.assertFalse(destination.exists())
            bad = root / "bad.db"
            bad.write_text("not sqlite")
            with self.assertRaises(sqlite3.Error):
                backup.copy_database(bad, destination)
            self.assertFalse(destination.exists())

    def test_foreign_key_failure_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / "source.db"
            with sqlite3.connect(source) as db:
                db.executescript("CREATE TABLE parent (id INTEGER PRIMARY KEY); CREATE TABLE child (id INTEGER REFERENCES parent(id)); INSERT INTO child VALUES (99);")
            with self.assertRaises(ValueError):
                backup.verify(source)


if __name__ == "__main__":
    unittest.main()
