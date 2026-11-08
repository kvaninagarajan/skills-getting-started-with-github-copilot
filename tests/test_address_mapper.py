"""Integration tests for Excel imports; no external geocoder is contacted."""

import io
import os
from pathlib import Path
import shutil
import socket
import sqlite3
import subprocess
import time
import zipfile
from xml.sax.saxutils import escape

import httpx
import pytest


ROOT = Path(__file__).resolve().parents[1]


def workbook(rows):
    sheet = []
    for number, values in enumerate(rows, 1):
        cells = []
        for column, value in enumerate(values):
            reference = f"{chr(65 + column)}{number}"
            if value is not None:
                cells.append(
                    f'<c r="{reference}" t="inlineStr"><is><t>'
                    f'{escape(str(value))}</t></is></c>'
                )
        sheet.append(f'<row r="{number}">{"".join(cells)}</row>')
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "[Content_Types].xml",
            '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
            '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
            "</Types>",
        )
        archive.writestr(
            "_rels/.rels",
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
            "</Relationships>",
        )
        archive.writestr(
            "xl/workbook.xml",
            '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            '<sheets><sheet name="Addresses" sheetId="1" r:id="rId1"/></sheets></workbook>',
        )
        archive.writestr(
            "xl/_rels/workbook.xml.rels",
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
            "</Relationships>",
        )
        archive.writestr(
            "xl/worksheets/sheet1.xml",
            '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            f'<sheetData>{"".join(sheet)}</sheetData></worksheet>',
        )
    return output.getvalue()


@pytest.fixture(scope="session")
def assembly():
    if not shutil.which("dotnet"):
        pytest.skip(".NET SDK is required for address mapper integration tests")
    subprocess.run(
        ["dotnet", "build", str(ROOT / "api/AddressMapper.Api.csproj")],
        check=True, capture_output=True, text=True,
    )
    return ROOT / "api/bin/Debug/net10.0/AddressMapper.Api.dll"


@pytest.fixture
def api(assembly, tmp_path):
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]
    environment = {
        **os.environ,
        "ConnectionStrings__Addresses": f"Data Source={tmp_path / 'addresses.db'}",
        "Geocoding__ContactEmail": "",
        "ASPNETCORE_ENVIRONMENT": "Production",
    }
    with (tmp_path / "server.log").open("w") as log:
        process = subprocess.Popen(
            ["dotnet", str(assembly), "--urls", f"http://127.0.0.1:{port}"],
            cwd=tmp_path, env=environment, stdout=log, stderr=log,
        )
        try:
            with httpx.Client(base_url=f"http://127.0.0.1:{port}", timeout=10) as client:
                for _ in range(100):
                    if process.poll() is not None:
                        pytest.fail((tmp_path / "server.log").read_text())
                    try:
                        if client.get("/api/addresses").status_code == 200:
                            break
                    except httpx.TransportError:
                        pass
                    time.sleep(0.1)
                else:
                    pytest.fail("Address API did not start")
                yield client, tmp_path / "addresses.db"
        finally:
            process.terminate()
            process.wait(timeout=10)


def upload(client, rows, geocode=False):
    return client.post(
        "/api/addresses/import",
        files={"file": ("addresses.xlsx", workbook(rows))},
        data={"geocode": str(geocode).lower()},
    )


def test_upload_map_assets_upsert_and_persistence(api):
    client, database = api
    assert "Address Mapper" in client.get("/").text
    assert client.get("/app.js").status_code == 200
    assert client.get("/styles.css").status_code == 200
    result = upload(client, [
        ["ADDRESSID", "address", "latitude", "longitude"],
        ["001", "<b>Example address</b>", "0", "0"],
        ["002", "Another address", None, None],
    ])
    assert result.status_code == 200, result.text
    assert result.json()["imported"] == 2
    assert result.json()["located"] == 1
    saved = client.get("/api/addresses").json()
    assert saved[0]["addressId"] == "001"
    assert saved[0]["latitude"] == 0
    assert saved[0]["address"] == "<b>Example address</b>"
    assert saved[1]["status"] == "notGeocoded"

    # Reuse a known location, but never reuse it for a changed address.
    assert upload(client, [["addressId", "address"], ["001", "<b>Example address</b>"]]).json()["located"] == 1
    assert upload(client, [["addressId", "address"], ["001", "Changed address"]]).json()["located"] == 0
    with sqlite3.connect(database) as connection:
        assert connection.execute("SELECT COUNT(*) FROM Addresses").fetchone()[0] == 2
        assert connection.execute(
            "SELECT Address, Latitude FROM Addresses WHERE AddressId = '001'"
        ).fetchone() == ("Changed address", None)


@pytest.mark.parametrize("rows", [
    [],
    [["addressId"], ["a"]],
    [["addressId", "address"], ["a", ""]],
    [["addressId", "address"], ["a", "Valid"], ["a", "Duplicate"]],
    [["addressId", "address", "latitude"], ["a", "Valid", "10"]],
    [["addressId", "address", "latitude", "longitude"], ["a", "Valid", "91", "0"]],
    [["addressId", "address", "latitude", "longitude"], ["a", "Valid", "0", "181"]],
    [["addressId", "address", "latitude", "longitude"], ["a", "Valid", "NaN", "0"]],
    [["addressId", "address", "latitude", "longitude"], ["a", "Valid", "0", ""]],
    [["addressId", "address"], ["a", "Valid"], ["b", "x" * 501]],
    [["addressId", "address"], *[[str(i), "Address"] for i in range(51)]],
])
def test_invalid_workbook_is_atomic(api, rows):
    client, _ = api
    response = upload(client, rows)
    assert response.status_code == 400, response.text
    assert response.json()["error"]
    assert client.get("/api/addresses").json() == []


def test_missing_corrupt_and_oversized_uploads(api):
    client, _ = api
    assert client.post("/api/addresses/import", json={}).status_code == 400
    for name, content in [
        ("addresses.xlsx", b"not a workbook"),
        ("addresses.csv", b"addressId,address"),
        ("addresses.xlsx", b"x" * (1024 * 1024 + 1)),
    ]:
        assert client.post(
            "/api/addresses/import", files={"file": (name, content)}
        ).status_code == 400
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("large.xml", "x" * (10 * 1024 * 1024 + 1))
    assert client.post(
        "/api/addresses/import", files={"file": ("large.xlsx", output.getvalue())}
    ).status_code == 400
    assert client.get("/api/addresses").json() == []


def test_unconfigured_geocoder_is_explicit(api):
    client, _ = api
    response = upload(client, [["addressId", "address"], ["a", "Unknown address"]], geocode=True)
    assert response.status_code == 200
    assert response.json()["located"] == 0
    assert response.json()["addresses"][0]["status"] == "geocodingUnavailable"
