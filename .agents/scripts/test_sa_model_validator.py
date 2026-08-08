#!/usr/bin/env python3
"""Tests for validate_model.py. Run: python test_sa_model_validator.py"""
import copy
import os
import sys

from validate_model import validate


def base_model():
    """Smallest fully-valid model (bookstore subset)."""
    return {
        "meta": {"project": "bookstore", "org": "Codex", "language": "th",
                 "profile": "academic", "authors": ["Pon"], "date": "2026-07-06"},
        "problem": {
            "current_problems": [{"id": "P1", "text": "ลูกค้าไม่รู้ว่ามีสินค้าไหม"}],
            "objectives": [{"id": "O1", "text": "แสดงสต็อกจริง", "problems": ["P1"]}],
            "benefits": [{"id": "B1", "text": "ขายได้มากขึ้น", "objectives": ["O1"]}],
        },
        "actors": [{"id": "ACT-CUST", "name": "ลูกค้า", "desc": "ผู้ซื้อ"},
                   {"id": "ACT-SALE", "name": "พนักงานขาย", "desc": "ผู้ขาย"}],
        "scope": [{"actor": "ACT-CUST", "capability": "สั่งซื้อสินค้า",
                   "use_cases": ["UC-ORDER"]}],
        "use_cases": [{
            "id": "UC-ORDER", "name": "สั่งซื้อสินค้า",
            "actors": ["ACT-CUST"], "objectives": ["O1"],
            "preconditions": ["ล็อกอินแล้ว"],
            "postconditions": ["ใบสั่งซื้อถูกบันทึกและสต็อกถูกจอง"],
            "main_flow": [{"step": 1, "actor": "ACT-CUST",
                           "action": "เพิ่มสินค้าลงตะกร้า",
                           "system_response": "แสดงตะกร้า",
                           "fields": ["ENT-PRODUCT.stock_qty"]}],
            "extensions": [{"at_step": 1, "condition": "สินค้าหมด",
                            "flow": "ระบบแจ้งว่าหมดและเสนอสินค้าใกล้เคียง",
                            "fields": []}],
            "special_reqs": [], "entities": ["ENT-PRODUCT", "ENT-ORDER"],
            "screens": ["SCR-CART"],
        }],
        "entities": [
            {"id": "ENT-PRODUCT", "name": "สินค้า", "fields": [
                {"name": "product_id", "type": "string", "size": 20,
                 "desc": "รหัสสินค้า", "pk": True},
                {"name": "stock_qty", "type": "integer", "size": 10,
                 "desc": "จำนวนคงเหลือ"}]},
            {"id": "ENT-ORDER", "name": "ใบสั่งซื้อ", "fields": [
                {"name": "order_id", "type": "string", "size": 20,
                 "desc": "เลขใบสั่งซื้อ", "pk": True},
                {"name": "product_id", "type": "string", "size": 20,
                 "desc": "สินค้า", "fk": "ENT-PRODUCT.product_id"},
                {"name": "order_status", "type": "string", "size": 20,
                 "desc": "สถานะ"}]},
        ],
        "states": [{"entity": "ENT-ORDER", "field": "order_status",
                    "states": ["awaiting_payment", "paid", "shipped"],
                    "transitions": [{"from": "awaiting_payment", "to": "paid",
                                     "trigger": "ยืนยันชำระเงิน", "uc": "UC-ORDER"}]}],
        "nfrs": [{"id": "NFR-1", "category": "performance",
                  "requirement": "หน้า login ตอบสนอง", "metric": "<= 3 วินาที"}],
        "security": [],
        "architecture": {"style": "web client-server",
                         "components": [{"name": "web", "responsibility": "UI"}],
                         "deployment": "single VM"},
        "screens": [{"id": "SCR-CART", "name": "ตะกร้าสินค้า",
                     "use_cases": ["UC-ORDER"]}],
        "plan": {"phases": [{"name": "วิเคราะห์", "from": "2026-07", "to": "2026-08"},
                            {"name": "ออกแบบ", "from": "2026-09", "to": "2026-10"}]},
        "budget": [{"item": "Server", "category": "hardware", "amount": 20000}],
        "literature": [{"topic": "payment gateway", "source": "ธนาคารกรุงเทพ",
                        "relevance": "ช่องทางชำระเงิน"}],
    }


def mutate(**overrides):
    m = copy.deepcopy(base_model())
    m.update(overrides)
    return m


def test_clean_model_passes():
    r = validate(base_model())
    assert r.ok, f"clean model must pass, got errors: {r.errors}"


def test_e1_unknown_actor_in_use_case():
    m = base_model()
    m["use_cases"][0]["actors"] = ["ACT-GHOST"]
    r = validate(m)
    assert any(rule == "E1" for rule, _ in r.errors), r.errors


def test_e1_unknown_actor_in_scope():
    m = base_model()
    m["scope"][0]["actor"] = "ACT-GHOST"
    r = validate(m)
    assert any(rule == "E1" for rule, _ in r.errors), r.errors


def test_e2_scope_without_use_case():
    m = base_model()
    m["scope"].append({"actor": "ACT-CUST", "capability": "ตั้งกระทู้",
                       "use_cases": []})
    r = validate(m)
    assert any(rule == "E2" for rule, _ in r.errors), r.errors


def test_e2_scope_dangling_use_case():
    m = base_model()
    m["scope"][0]["use_cases"] = ["UC-GHOST"]
    r = validate(m)
    assert any(rule == "E2" for rule, _ in r.errors), r.errors


def test_e6_duplicate_ids():
    m = base_model()
    m["actors"].append({"id": "ACT-CUST", "name": "ซ้ำ", "desc": "ซ้ำ"})
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_objective_problem():
    m = base_model()
    m["problem"]["objectives"][0]["problems"] = ["P-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_benefit_objective():
    m = base_model()
    m["problem"]["benefits"][0]["objectives"] = ["O-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_uc_objective():
    m = base_model()
    m["use_cases"][0]["objectives"] = ["O-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_uc_entity():
    m = base_model()
    m["use_cases"][0]["entities"] = ["ENT-GHOST"]
    r = validate(m)
    # E4 also uses `entities` for its allowed-fields set, so make sure the
    # dangling-ref check still fires independently of whatever E4 does here.
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_uc_screen():
    m = base_model()
    m["use_cases"][0]["screens"] = ["SCR-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_screen_uc():
    m = base_model()
    m["screens"][0]["use_cases"] = ["UC-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_transition_state():
    m = base_model()
    m["states"][0]["transitions"][0]["to"] = "ghost_state"
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e3_fk_targets_missing_field():
    m = base_model()
    m["entities"][1]["fields"].append(
        {"name": "payment_id", "type": "string", "size": 20,
         "desc": "การชำระเงิน", "fk": "ENT-PAYMENT.payment_id"})
    r = validate(m)
    assert any(rule == "E3" for rule, _ in r.errors), r.errors


def test_e4_step_field_not_on_entities():
    m = base_model()
    m["use_cases"][0]["main_flow"][0]["fields"] = ["ENT-PRODUCT.ghost_field"]
    r = validate(m)
    assert any(rule == "E4" for rule, _ in r.errors), r.errors


def test_e4_extension_at_step_not_in_flow():
    m = base_model()
    m["use_cases"][0]["extensions"][0]["at_step"] = 999
    r = validate(m)
    assert any(rule == "E4" for rule, _ in r.errors), r.errors


def test_e5_boolean_field_cannot_hold_three_states():
    m = base_model()
    m["entities"][1]["fields"][2]["type"] = "boolean"
    r = validate(m)
    assert any(rule == "E5" for rule, _ in r.errors), r.errors


def test_e5_states_field_missing():
    m = base_model()
    m["states"][0]["field"] = "ghost_status"
    r = validate(m)
    assert any(rule == "E5" for rule, _ in r.errors), r.errors


def test_e5_transition_unknown_uc():
    m = base_model()
    m["states"][0]["transitions"][0]["uc"] = "UC-GHOST"
    r = validate(m)
    assert any(rule == "E5" for rule, _ in r.errors), r.errors


def test_e7_plan_months_not_contiguous():
    m = base_model()
    # from 2026-11 while to stays 2026-10 -> E7 fires (starts-after-ends and/or gap)
    m["plan"]["phases"][1]["from"] = "2026-11"
    r = validate(m)
    assert any(rule == "E7" for rule, _ in r.errors), r.errors


def test_e7_month_out_of_range():
    m = base_model()
    m["plan"]["phases"][0]["from"] = "2026-13"
    r = validate(m)
    # Assert on the message so this gates the bounds check specifically: without
    # it, "2026-13" would still trip "starts after it ends" (a false pass).
    assert any(rule == "E7" and "malformed month" in msg
               for rule, msg in r.errors), r.errors


def test_e8_professional_requires_security():
    m = base_model()
    m["meta"]["profile"] = "professional"
    m["security"] = []
    r = validate(m)
    assert any(rule == "E8" for rule, _ in r.errors), r.errors


def test_w1_use_case_without_scope_and_objective_without_uc():
    m = base_model()
    m["use_cases"].append({
        "id": "UC-ORPHAN", "name": "ลอย", "actors": ["ACT-SALE"],
        "preconditions": [], "postconditions": ["x"], "main_flow": [],
        "extensions": [], "special_reqs": [], "entities": [], "screens": []})
    m["problem"]["objectives"].append({"id": "O2", "text": "ไม่มีใครทำ",
                                       "problems": ["P1"]})
    r = validate(m)
    w = [rule for rule, _ in r.warnings]
    assert w.count("W1") >= 2, r.warnings


def test_w2_screen_coverage():
    m = base_model()
    m["screens"].append({"id": "SCR-ORPHAN", "name": "จอลอย", "use_cases": []})
    r = validate(m)
    assert any(rule == "W2" for rule, _ in r.warnings), r.warnings


def test_w3_money_types_inconsistent():
    m = base_model()
    m["entities"][0]["fields"].append({"name": "unit_price", "type": "integer",
                                       "size": 10, "desc": "ราคา"})
    m["entities"][1]["fields"].append({"name": "total_price", "type": "decimal",
                                       "size": 9, "desc": "ราคารวม"})
    r = validate(m)
    assert any(rule == "W3" for rule, _ in r.warnings), r.warnings


def test_w4_trigger_shaped_postcondition():
    m = base_model()
    m["use_cases"][0]["postconditions"] = ["กดปุ่มยืนยันการแก้ไขข้อมูล"]
    r = validate(m)
    assert any(rule == "W4" for rule, _ in r.warnings), r.warnings


def test_w5_copy_pasted_extensions():
    m = base_model()
    boiler = "ระบบขัดข้อง ให้ restart เครื่องแล้วเริ่มใหม่"
    for i in range(3):
        m["use_cases"].append({
            "id": f"UC-C{i}", "name": f"c{i}", "actors": ["ACT-SALE"],
            "preconditions": [], "postconditions": ["x"], "main_flow": [],
            "extensions": [{"at_step": 1, "condition": "ขัดข้อง",
                            "flow": boiler, "fields": []}],
            "special_reqs": [], "entities": [], "screens": []})
    r = validate(m)
    assert any(rule == "W5" for rule, _ in r.warnings), r.warnings


def test_w6_sample_exceeds_size_and_empty_entity():
    m = base_model()
    m["entities"][0]["fields"][0]["sample"] = "ศาสตร์การปฏิญาณเพื่อการบำบัดรักษา33"
    m["entities"].append({"id": "ENT-EMPTY", "name": "ว่าง", "fields": []})
    r = validate(m)
    w = [rule for rule, _ in r.warnings]
    assert w.count("W6") >= 2, r.warnings


def test_e9_missing_meta():
    m = base_model()
    del m["meta"]
    r = validate(m)
    assert any(rule == "E9" for rule, _ in r.errors), r.errors


def test_e9_invalid_language():
    m = base_model()
    m["meta"]["language"] = "jp"
    r = validate(m)
    assert any(rule == "E9" for rule, _ in r.errors), r.errors


def test_e9_misspelled_profile_does_not_slip_past_e8():
    # 'profesional' is not 'professional', so E8's security gate never fires;
    # E9 must catch the typo instead of letting a security-less doc generate.
    m = base_model()
    m["meta"]["profile"] = "profesional"
    m["security"] = []
    r = validate(m)
    assert any(rule == "E9" for rule, _ in r.errors), r.errors


def test_e9_missing_project():
    m = base_model()
    m["meta"]["project"] = ""
    r = validate(m)
    assert any(rule == "E9" for rule, _ in r.errors), r.errors


def test_e10_relationship_unknown_entity():
    m = base_model()
    m["relationships"] = [{"from": "ENT-PRODUCT", "to": "ENT-GHOST",
                           "type": "association"}]
    r = validate(m)
    assert any(rule == "E10" for rule, _ in r.errors), r.errors


def test_e10_relationship_unknown_type():
    m = base_model()
    m["relationships"] = [{"from": "ENT-ORDER", "to": "ENT-PRODUCT",
                           "type": "has-a-bunch-of"}]
    r = validate(m)
    assert any(rule == "E10" for rule, _ in r.errors), r.errors


def test_e10_valid_relationship_passes():
    m = base_model()
    m["relationships"] = [{"from": "ENT-ORDER", "to": "ENT-PRODUCT",
                           "type": "association", "from_card": "*",
                           "to_card": "1", "label": "orders"}]
    r = validate(m)
    assert not any(rule == "E10" for rule, _ in r.errors), r.errors


def test_e10_relationship_type_tbd_is_tolerated():
    # TBD is a legal, inventoried value for any leaf — an undecided relationship
    # type must not block generation, it must be reported as a TBD.
    m = base_model()
    m["relationships"] = [{"from": "ENT-ORDER", "to": "ENT-PRODUCT",
                           "type": "TBD"}]
    r = validate(m)
    assert not any(rule == "E10" for rule, _ in r.errors), r.errors
    assert any("type" in p for p in r.tbds), r.tbds


def test_e9_non_string_language_reports_not_crashes():
    # a scalar written as a one-element list is a common YAML slip; it must
    # yield a clean E9, not a TypeError out of the gate.
    m = base_model()
    m["meta"]["language"] = ["th"]
    r = validate(m)   # must not raise
    assert any(rule == "E9" for rule, _ in r.errors), r.errors


def test_e10_non_string_entity_ref_reports_not_crashes():
    m = base_model()
    m["relationships"] = [{"from": ["ENT-ORDER"], "to": "ENT-PRODUCT",
                           "type": "association"}]
    r = validate(m)   # must not raise on the unhashable ref
    assert any(rule == "E10" for rule, _ in r.errors), r.errors


def test_e6_dangling_nfr_objective():
    m = base_model()
    m["nfrs"][0]["objectives"] = ["O-GHOST"]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_dangling_security_use_case():
    m = base_model()
    m["meta"]["profile"] = "professional"
    m["security"] = [{"id": "SEC-1", "concern": "ข้อมูลบัตร",
                      "control": "ส่งต่อ gateway", "use_cases": ["UC-GHOST"]}]
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_e6_duplicate_scope_fr_ids():
    m = base_model()
    m["scope"][0]["id"] = "FR-1"
    m["scope"].append({"id": "FR-1", "actor": "ACT-CUST",
                       "capability": "อีกอย่าง", "use_cases": ["UC-ORDER"]})
    r = validate(m)
    assert any(rule == "E6" for rule, _ in r.errors), r.errors


def test_w7_orphan_entity():
    m = base_model()
    m["entities"].append({"id": "ENT-LONELY", "name": "เหงา", "fields": [
        {"name": "x_id", "type": "string", "size": 10, "desc": "x",
         "pk": True}]})
    r = validate(m)
    assert any(rule == "W7" for rule, _ in r.warnings), r.warnings


def test_w8_entity_without_primary_key():
    m = base_model()
    # strip the pk flag off ENT-PRODUCT's key field
    m["entities"][0]["fields"][0].pop("pk")
    r = validate(m)
    assert any(rule == "W8" for rule, _ in r.warnings), r.warnings


def test_w9_nfr_without_metric():
    m = base_model()
    m["nfrs"].append({"id": "NFR-2", "category": "usability",
                      "requirement": "ใช้ง่าย", "metric": ""})
    r = validate(m)
    assert any(rule == "W9" for rule, _ in r.warnings), r.warnings


def test_tbd_inventory():
    m = base_model()
    m["architecture"]["deployment"] = "TBD"
    r = validate(m)
    assert any("deployment" in p for p in r.tbds), r.tbds


FIXTURE = os.path.join(os.path.dirname(__file__), "fixtures",
                       "sa-model-bookstore.yaml")


def _load_fixture():
    from validate_model import load_model
    return load_model(FIXTURE)


def test_fixture_is_clean():
    r = validate(_load_fixture())
    assert r.ok, r.errors
    assert not r.warnings, r.warnings


def test_seeded_defects_are_caught():
    """Each mutation = a real defect found in the Project SA.pdf review."""
    # 1. scope capability with no use case (the forgotten forum feature)
    m = _load_fixture()
    m["scope"][1]["use_cases"] = []
    assert any(rule == "E2" for rule, _ in validate(m).errors)

    # 2. dangling FK (Order.payment_id -> field that does not exist)
    m = _load_fixture()
    m["entities"][1]["fields"].append(
        {"name": "payment_id", "type": "string", "size": 20,
         "desc": "การชำระเงิน", "fk": "ENT-PAYMENT.payment_id"})
    assert any(rule == "E3" for rule, _ in validate(m).errors)

    # 3. boolean deliver_status vs a 3+-state lifecycle
    m = _load_fixture()
    m["entities"][1]["fields"][3]["type"] = "boolean"
    assert any(rule == "E5" for rule, _ in validate(m).errors)

    # 4. identical boilerplate extension pasted across >= 3 use cases
    m = _load_fixture()
    boiler = "กรณีระบบขัดข้อง ให้ restart เครื่องคอมพิวเตอร์แล้ว login ใหม่"
    for uc in m["use_cases"][:3]:
        uc["extensions"].append({"at_step": 1, "condition": "ระบบขัดข้อง",
                                 "flow": boiler, "fields": []})
    assert any(rule == "W5" for rule, _ in validate(m).warnings)

    # 5. String(20) product name vs the 33-char sample from the review
    m = _load_fixture()
    m["entities"][0]["fields"][1]["size"] = 20
    assert any(rule == "W6" for rule, _ in validate(m).warnings)


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
