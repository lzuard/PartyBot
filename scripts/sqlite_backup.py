#!/usr/bin/env python3
"""SQLite-aware backup/restore to a NEW file. Never overwrites an existing database."""
import argparse
import os
from pathlib import Path
import sqlite3
import sys


def connect_readonly(path):
    return sqlite3.connect(Path(path).resolve().as_uri() + "?mode=ro", uri=True, timeout=30)


def check(connection):
    if connection.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
        raise ValueError("SQLite integrity_check failed")
    if connection.execute("PRAGMA foreign_key_check").fetchone() is not None:
        raise ValueError("SQLite foreign_key_check failed")


def verify(path):
    connection = connect_readonly(path)
    try:
        check(connection)
    finally:
        connection.close()


def copy_database(source, destination):
    target = Path(destination).absolute()
    reader = connect_readonly(source)
    created = False
    try:
        # Keep a consistent read snapshot, including committed WAL contents.
        reader.execute("BEGIN")
        check(reader)
        descriptor = os.open(target, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        created = True
        os.close(descriptor)
        writer = sqlite3.connect(target)
        try:
            reader.backup(writer, pages=256, sleep=0.1)
            check(writer)
        finally:
            writer.close()
        reader.rollback()
    except Exception:
        if created:
            target.unlink(missing_ok=True)
        raise
    finally:
        reader.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["backup", "restore", "verify"])
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path, nargs="?")
    args = parser.parse_args()
    if args.operation == "verify":
        if args.destination is not None:
            parser.error("verify takes only source")
        verify(args.source)
    else:
        if args.destination is None:
            parser.error("backup/restore requires a new destination path")
        copy_database(args.source, args.destination)
    print("SQLite integrity and foreign keys: OK")


if __name__ == "__main__":
    try:
        main()
    except (sqlite3.Error, OSError, ValueError) as error:
        print(f"Backup operation failed: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(1)
