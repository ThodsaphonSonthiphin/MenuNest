#!/usr/bin/env python3
"""Tests for check_doc_provenance.py. Run: python test_check_doc_provenance.py"""
import os
import sys
import tempfile

from check_doc_provenance import (normalize, numbers_in, model_numbers,
                                   doc_findings, main)

MODEL = {
    "meta": {"project": "bookstore", "date": "2026-07-06"},
    "entities": [{"id": "ENT-PRODUCT", "fields": [
        {"name": "product_name", "type": "string", "size": 120},
        {"name": "unit_price", "type": "decimal", "size": 9}]}],
    "nfrs": [{"id": "NFR-1", "metric": "<= 3 วินาที"}],
    "budget": [{"item": "Server", "amount": 20000}],
}


def _finds(md, model=MODEL, source=None):
    allowed = model_numbers(model)
    if source:
        allowed |= numbers_in(source)
    return doc_findings(md, allowed)


def test_normalize_collapses_ints_and_strips_separators():
    assert normalize("07") == "7"
    assert normalize("20,000") == "20000"
    assert normalize("99.9%") == "99.9"
    assert normalize("3") == "3"


def test_model_numbers_walks_every_scalar():
    nums = model_numbers(MODEL)
    assert {"2026", "7", "6", "120", "9", "3", "20000"} <= nums


def test_faithful_document_has_no_findings():
    md = ("# bookstore\n\nผู้จัดทำ 2026-07-06\n\n"
          "หน้า login ตอบสนองภายใน 3 วินาที และงบ 20,000 บาท "
          "ฟิลด์ยาว 120 ตัวอักษร\n")
    assert _finds(md) == []


def test_invented_metric_is_flagged():
    md = "ระบบรองรับผู้ใช้ 99.9% และตอบสนองใน 2 วินาที\n"
    toks = {t for _, t, _ in _finds(md)}
    assert "99.9" in toks       # invented percentage
    assert "2" in toks          # invented threshold (model says 3)


def test_thousands_separator_matches_model():
    # model stores 20000; document writes 20,000 — must be accepted
    assert _finds("ต้นทุนรวม 20,000 บาท\n") == []


def test_code_and_mermaid_fences_are_ignored():
    md = ("```mermaid\nflowchart TD\n  A[\"999 nodes\"] --> B[\"42\"]\n```\n\n"
          "```python\nx = 123456\n```\n\nและใช้ `size = 7777`\n")
    assert _finds(md) == []


def test_structural_numbers_are_exempt():
    md = ("## 7.1 Data model\n\n1. ขั้นแรก\n2. ขั้นสอง\n\n"
          "| # | Field |\n|---|---|\n| 1 | a |\n| 13 | b |\n\n"
          "ดู FR-1, UC-2, NFR-1, TC-UC-ORDER-1, P1, SEC-1\n")
    assert _finds(md) == []


def test_source_flag_accepts_input_only_numbers():
    md = "ราคาต่อชิ้น 350 บาท\n"
    assert {t for _, t, _ in _finds(md)} == {"350"}          # not in model
    assert _finds(md, source="ราคาขาย 350 บาทต่อชิ้น") == []  # but in the source


def test_main_report_vs_strict(tmp_path=None):
    with tempfile.TemporaryDirectory() as d:
        model_p = os.path.join(d, "m.yaml")
        doc_p = os.path.join(d, "doc.md")
        with open(model_p, "w", encoding="utf-8") as f:
            f.write("meta:\n  project: x\nbudget:\n  - {item: a, amount: 20000}\n")
        with open(doc_p, "w", encoding="utf-8") as f:
            f.write("งบ 20000 บาท แต่กำไร 99.9%\n")   # 99.9 is invented
        assert main([doc_p, model_p]) == 0            # report mode: exit 0
        assert main([doc_p, model_p, "--strict"]) == 1  # strict: exit 1
        # a fully faithful doc passes even under --strict
        with open(doc_p, "w", encoding="utf-8") as f:
            f.write("งบ 20000 บาท\n")
        assert main([doc_p, model_p, "--strict"]) == 0


TESTS = [v for k, v in sorted(globals().items()) if k.startswith("test_")]

if __name__ == "__main__":
    failed = 0
    for t in TESTS:
        try:
            t()
            print(f"PASS  {t.__name__}")
        except AssertionError as e:
            failed += 1
            print(f"FAIL  {t.__name__}: {e}")
    print(f"{len(TESTS) - failed}/{len(TESTS)} passed")
    sys.exit(1 if failed else 0)
