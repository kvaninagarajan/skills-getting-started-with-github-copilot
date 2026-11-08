# Getting Started with GitHub Copilot

_Get started using GitHub Copilot in less than an hour._

## Excel address mapping project

The separate .NET 10 application uses three folders:

- `model`: address records, SQLite EF Core context, and generated migrations.
- `api`: ASP.NET Core upload/list endpoints, Excel validation, and geocoding.
- `web`: Excel upload form, searchable address list, and Leaflet/OpenStreetMap map.

The original Python school-activities example is unchanged.

### Run locally

Install the .NET 10 SDK, then run from the repository root:

```bash
dotnet tool restore
dotnet restore api/AddressMapper.Api.csproj
dotnet ef database update --project model --startup-project api
dotnet run --project api --no-launch-profile --urls http://localhost:5000
```

Open <http://localhost:5000>. Startup also applies committed EF Core migrations.
SQLite stores records in `addresses.db` in the process working directory; keep that
directory consistent between the migration and run commands. Override it with
the `ConnectionStrings__Addresses` environment variable if needed.

For address-only geocoding, set `Geocoding__ContactEmail` to your real contact
email before running the app (Nominatim requires an identifying User-Agent),
then check the consent box in the upload form. The email is sent to the geocoder.
Without configuration or consent, uploads still work, but only rows with supplied
coordinates (or unchanged, previously located addresses) receive pins.
Do not commit contact details or uploaded address data.

### Workbook format

Upload an `.xlsx` workbook whose **first worksheet** begins with these headers
(case-insensitive, column order does not matter):

| addressId | address | latitude (optional) | longitude (optional) |
| --- | --- | --- | --- |
| LOC-001 | 350 Fifth Avenue, New York, NY | 40.7484 | -73.9857 |
| LOC-002 | 10 Downing Street, London, UK | | |

Use literal cell values, not formulas. Format IDs as **Text** in Excel to
preserve leading zeros. Both coordinate columns must be present together, and
each row must supply both values or leave both empty. Empty rows are ignored.
Imports are limited to 1 MB, 50 addresses, 1,000 worksheet rows, and 10 MB of
uncompressed workbook contents. IDs must be unique within a workbook; an existing
ID updates its saved address instead of creating a duplicate. Invalid workbooks
return HTTP 400 without saving any rows.

Geocoding is approximate, not Geotab integration or vehicle tracking. Requests
are sequential, limited to less than one per second, and previously located
unchanged records are reused. Unmatched addresses and provider failures are
listed explicitly; re-upload to retry. For bulk/production use, replace the
demo geocoder with a suitable provider and follow the
[Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/).
Do not upload sensitive addresses to the public geocoder. The map requires
internet access for Leaflet and OpenStreetMap tiles.

API endpoints:

- `GET /api/addresses`: all saved records and their location status.
- `POST /api/addresses/import`: multipart `file` (`.xlsx`) and optional
  `geocode=true` (explicit permission to send addresses to Nominatim).

This is a **local demo**, with no authentication or per-user isolation. Keep it
bound to localhost; add authentication, authorization, HTTPS, anti-forgery
protection, and appropriate data-retention controls before exposing it publicly.

### Validate

```bash
dotnet build api/AddressMapper.Api.csproj
node --check web/app.js
python -m pytest tests/test_address_mapper.py
```

The integration tests use the existing pytest/httpx tooling and temporary SQLite
databases. They do not call an external geocoder. For future schema changes,
use `dotnet ef migrations add <Name> --project model --startup-project api`.

## Welcome

- **Who is this for**: Developers at any experience level looking to accelerate their code workflow.
- **What you'll learn**: The different ways to interact with Copilot to explain, write, plan, and develop code.
- **What you'll build**: You will guide Copilot to update Mergington High School's extracurricular activities website.
- **Prerequisites**:
  - Skills exercise: [Introduction to GitHub](https://github.com/skills/introduction-to-github)
  - Familiarity with [VS Code](https://code.visualstudio.com/)
  - Basic coding principles
- **How long**: This exercise takes less than one hour to complete.

In this exercise, you will:

1. Use a preconfigured Codespace to run VS Code in your browser.
1. Learn different interaction options to develop and plan with GitHub Copilot.
1. Use Copilot to summarize and review your pull request.

### How to start this exercise

Simply copy the exercise to your account, then give your favorite Octocat (Mona) **about 20 seconds** to prepare the first lesson, then **refresh the page**.

[![](https://img.shields.io/badge/Copy%20Exercise-%E2%86%92-1f883d?style=for-the-badge&logo=github&labelColor=197935)](https://github.com/new?template_owner=skills&template_name=getting-started-with-github-copilot&owner=%40me&name=skills-getting-started-with-github-copilot&description=Exercise:+Get+started+using+GitHub+Copilot&visibility=public)

<details>
<summary>Having trouble? 🤷</summary><br/>

When copying the exercise, we recommend the following settings:

- For owner, choose your personal account or an organization to host the repository.

- We recommend creating a public repository, since private repositories will use Actions minutes.
   
If the exercise isn't ready in 20 seconds, please check the [Actions](../../actions) tab.

- Check to see if a job is running. Sometimes it simply takes a bit longer.

- If the page shows a failed job, please submit an issue. Nice, you found a bug! 🐛

</details>

---

&copy; 2025 GitHub &bull; [Code of Conduct](https://www.contributor-covenant.org/version/2/1/code_of_conduct/code_of_conduct.md) &bull; [MIT License](https://gh.io/mit)