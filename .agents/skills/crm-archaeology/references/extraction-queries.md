# Extraction Queries — verified commands and queries by step

All commands/queries below were verified against official Microsoft Learn docs
(June 2026). `GET /...` URLs are relative to the environment URL
(`https://<org>.crm*.dynamics.com`) and need a bearer token plus headers
`Accept: application/json`, `OData-MaxVersion: 4.0`, `OData-Version: 4.0`.
Add `Prefer: odata.include-annotations="*"` to get choice labels alongside
numeric values. Everything here is read-only.

## Table of contents

- [Step 0 — Tooling and auth](#step-0--tooling-and-auth)
- [Step 1 — Solution export to disk](#step-1--solution-export-to-disk)
- [Step 2 — Entity universe](#step-2--entity-universe)
- [Step 3 — Data model detail](#step-3--data-model-detail)
- [Step 4 — Server-side automation](#step-4--server-side-automation)
- [Step 5 — Client-side logic](#step-5--client-side-logic)
- [Step 6 — Command bar, both generations](#step-6--command-bar-both-generations)
- [Step 7 — Security model](#step-7--security-model)
- [Step 7b — Embedded Power BI detection](#step-7b--embedded-power-bi-detection)
- [Step 8 — Runtime verification tools](#step-8--runtime-verification-tools)

---

## Step 0 — Tooling and auth

```powershell
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
pac auth create --environment "https://yourorg.crm.dynamics.com"
pac auth list / pac auth select --index N      # switch profiles
pac org who                                    # ALWAYS verify target before starting
```

Environment URL: make.powerapps.com → gear icon → Session details → Instance URL.

Bearer token for the Web API `GET`s below — prefer `dataverse:dv-connect` or an
existing project auth helper when available; otherwise:

```powershell
az login
az account get-access-token --resource "https://yourorg.crm.dynamics.com" --query accessToken -o tsv
```

(The `--resource` is the environment URL itself.)

## Step 1 — Solution export to disk

```powershell
pac solution list                              # SolutionUniqueName column; publisher prefix marks the org's own
pac solution export --name <SolutionUniqueName> --path .\export\Sol.zip --overwrite
# large solutions: add --async --max-async-wait-time 60
pac solution unpack --zipfile .\export\Sol.zip --folder .\src --allowWrite --allowDelete --clobber
```

What lands where (XML format, the default):

| Path | Contents |
|---|---|
| `WebResources\` | every JS/HTML/React web resource as a real file |
| `Controls\<Namespace.Name>\` | PCF components: `ControlManifest.xml` + `bundle.js` + `*.data.xml` |
| `Other\Customizations.xml` | entities, attributes, FormXml, views, RibbonDiffXml, option sets, sitemap |
| `Other\Solution.xml` | manifest |
| `PluginAssemblies\**\` | plugin DLLs |
| `AppModule*` files | app definition + `AppModuleSiteMap` (the left navigation) |
| Workflow files | classic workflows / business rules / BPFs as XAML |

Watch out:
- **Cloud flows and canvas apps do NOT decompose in the XML unpack.** When the
  org has cloud flows or Power Fx commands, also run
  `pac solution clone --name <X> --outputDirectory .\src-yaml` — the YAML layout
  adds `modernflows/` (readable flow JSON) and `canvasapps/<schema>/<name>.msapp`.
- Customizations living only in the **Default Solution** must first be added to
  an exportable unmanaged solution (maker portal → Solutions → New → Add existing).
- Re-unpacking into the same folder only rewrites changed files — that is the
  documented source-control workflow; commit after each re-extract.
- Managed ISV solutions export readable metadata but plugin logic stays compiled.

## Step 2 — Entity universe

Union FOUR sources; tag each entity with which sources hit.

**2a. App components (the superset of tables in the app):**
```
GET /api/data/v9.2/appmodules?$select=name,uniquename,appmoduleid,appmoduleidunique
GET /api/data/v9.0/RetrieveAppComponents(AppModuleId=<appmoduleid>)
```
Rows with `componenttype eq 1` are tables; resolve each `objectid`:
```
GET /api/data/v9.2/EntityDefinitions(<objectid>)?$select=LogicalName,SchemaName,DisplayName
```
Watch out: querying `appmodulecomponents` directly filters on
`_appmoduleidunique_value eq <appmoduleidunique>` — the join key is the app's
**`appmoduleidunique`**, NOT `appmoduleid`; the wrong GUID silently returns
zero rows. `GET /appmodules` returns only PUBLISHED apps (drafts need
`RetrieveUnpublishedMultiple`).

**2b. SiteMap (navigation subset + business taxonomy):**
Parse `AppModuleSiteMap` from the unpack, or:
```
GET /api/data/v9.2/sitemaps?$select=sitemapname,sitemapnameunique,isappaware,sitemapxml
```
`<Area>/<Group>/<SubArea Entity="...">` — every `Entity=` is a table users
navigate to. Keep the Area/Group display names: they are the business domains
and become the documentation chapters. An "Administration"-style group marks
master/reference data. SubAreas are security-trimmed at runtime.

**2c. Standard tables the business customized:**
```
GET /api/data/v9.2/EntityDefinitions?$select=LogicalName&$filter=IsCustomEntity eq false&$expand=Attributes($select=LogicalName,SchemaName,IsManaged;$filter=IsCustomAttribute eq true)
```
Drop entities whose `Attributes` array comes back empty — that filter must be
client-side (no lambda operators on metadata queries). `IsManaged` + publisher
prefix separates the org's own columns from installed ISV columns.

**2d. Tables with data / usage:**
```
GET /api/data/v9.2/RetrieveTotalRecordCount(EntityNames=@p1)?@p1=["account","contact","quote"]
```
Returns a logical-name → count dictionary. Counts come from a snapshot up to
**24 hours old** — confirm "empty" with a `$top=1` probe. Batch the names for
URL-length safety. Cross-check business usage in Power Platform admin center →
Dataverse analytics → "Most Used OOB / Custom Entities" (API-call volume).

**Noise filter** for any standard-entity listing:
`IsIntersect eq false and IsPrivate eq false and IsValidForAdvancedFind eq true`
(the last is a heuristic for user-facing — review, don't trust blindly).

Finally expand outward: from each entity follow its lookups (step 3
relationships) to catch child/line tables that never appear in navigation.

## Step 3 — Data model detail

Per entity:
```
GET /api/data/v9.2/EntityDefinitions(LogicalName='<table>')?$select=LogicalName&$expand=Attributes($select=LogicalName,AttributeType,RequiredLevel)
GET /api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$select=SchemaName,ReferencedEntity,ReferencedAttribute,ReferencingEntity,ReferencingAttribute&$filter=ReferencedEntity eq '<table>'
GET /api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata?$select=SchemaName,Entity1LogicalName,Entity2LogicalName,IntersectEntityName&$filter=Entity1LogicalName eq '<table>' or Entity2LogicalName eq '<table>'
```
Option-set labels (one cast query per enum type — casting to the abstract
`EnumAttributeMetadata` is NOT allowed):
```
GET /api/data/v9.2/EntityDefinitions(LogicalName='<table>')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$select=LogicalName&$expand=OptionSet,GlobalOptionSet
```
Repeat with `MultiSelectPicklistAttributeMetadata`, `StateAttributeMetadata`,
`StatusAttributeMetadata`, `BooleanAttributeMetadata`. Exactly one of
`OptionSet`/`GlobalOptionSet` is non-null (local vs global choice).
`statecode`/`statuscode` options are always local.

Watch out:
- Metadata queries have **no paging** — one response returns everything. Always
  `$select` narrowly and append `&LabelLanguages=1033` (or your LCID).
- `$filter` works only on primitive/enum properties; never on `DisplayName` or
  other Label complex types. Enum filters need the full namespace:
  `$filter=OwnershipType eq Microsoft.Dynamics.CRM.OwnershipTypes'UserOwned'`.
- `GlobalOptionSetDefinitions` supports retrieval only by Name or MetadataId —
  no `$filter`; harvest global choices via the per-attribute cast instead.
- ERD edges: 1:N rows give `ReferencingEntity.ReferencingAttribute` (the FK) →
  `ReferencedEntity` — exactly what a mermaid `erDiagram` line needs.

## Step 4 — Server-side automation

**Processes (one table for all):**
```
GET /api/data/v9.2/workflows?$filter=type eq 1 and statecode eq 1&$select=name,category,primaryentity,mode,scope,uniquename,ismanaged,triggeroncreate,triggerondelete,triggeronupdateattributelist,createstage,updatestage,deletestage&$orderby=category
```
`category`: 0 classic workflow · 1 dialog (deprecated) · 2 business rule ·
3 action · 4 business process flow · 5 cloud flow · 6 desktop flow · 7 AI flow.
Definition body: `$select=xaml` (classic/rules/BPF) or `$select=clientdata`
(cloud flows — JSON, Logic Apps schema; BPFs may populate both).

Watch out:
- `type eq 1` (Definition) is mandatory — real-time workflows and BPFs also
  create type-2 Activation rows that double-count.
- Business rule scope: *Entity* runs server-side (generated sync plugin);
  *All Forms* / specific form is client-side only. Record the scope.
- **"My Flows" outside solutions are not reliably in this table** — reconcile
  with the Power Automate portal. `api.flow.microsoft.com` is unsupported.

**Plugins and webhooks:**
```
GET /api/data/v9.2/sdkmessageprocessingsteps?$select=name,stage,mode,rank,filteringattributes,statecode,asyncautodelete&$expand=sdkmessageid($select=name),sdkmessagefilterid($select=primaryobjecttypecode),plugintypeid($select=friendlyname)
GET /api/data/v9.2/plugintypes?$select=friendlyname&$expand=pluginassemblyid($select=name)
```
`stage`: 10 pre-validation · 20 pre-operation · 40 post-operation. `mode`:
0 sync · 1 async (async only at post-op; capture both — they define rollback
semantics). `filteringattributes` gates Update triggers. Steps whose event
handler is a `serviceendpoint` are webhooks/Service Bus, not code plugins.
Filter out `ishidden` and Microsoft-managed steps — they vastly outnumber
custom ones.

## Step 5 — Client-side logic

**Form → event → function map:**
```
GET /api/data/v9.2/systemforms?$select=name,objecttypecode,type,formxml&$filter=formactivationstate eq 1
```
(`type eq 2` = Main forms; `objecttypecode` value is the table logical name.)
Parse from each `formxml`:
- `//formLibraries/Library/@name` — the JS web resources loaded by the form
- `//events/event[@name|@attribute]/Handlers/Handler[@libraryName|@functionName|@enabled|@parameters]`
  — OnLoad/OnSave at form level; OnChange events carry the column in the
  event's `attribute`. `<InternalHandlers>` are compiled business rules —
  include, but label separately.

**Web resources** (already on disk from step 1; for orgs studied without
export):
```
GET /api/data/v9.2/webresourceset?$select=name,displayname,webresourcetype,ismanaged,modifiedon&$filter=webresourcetype eq 3
GET /api/data/v9.2/webresourceset(<id>)?$select=name,content          # content = Base64 → decode to file
```
`webresourcetype`: 1 HTML · 2 CSS · 3 JS. Single file in a browser:
`<EnvURL>/WebResources/<name>` (latest *published* version). Names may contain
`/` (folder-style) — create directories when dumping.

**PCF vs HTML-hosted React:**
```
GET /api/data/v9.2/customcontrols?$select=name,version,ismanaged
GET /api/data/v9.2/customcontrols(<id>)?$select=name,manifest
```
In the manifest: `control-type="virtual"` = platform-React control (React is
NOT in its bundle — don't conclude "not React" from the bundle);
`<uses-feature name="WebAPI" required="true">` = proof it talks to Dataverse.
HTML-hosted React apps live in `WebResources\` grouped by folder-style name
prefix; JS-to-JS references create NO dependency records — group by prefix,
not by dependency queries.

**Minified bundle analysis** (entity names and queries survive minification):
```
rg 'Xrm\.WebApi|webAPI\.(retrieveMultipleRecords|retrieveRecord|createRecord|updateRecord|deleteRecord)\(|parent\.Xrm|GetGlobalContext|getClientUrl|/api/data/v9|\$select=|\$filter=|fetchXml|<fetch' bundle.js
```
The first string argument of `Xrm.WebApi.*` / `context.webAPI.*` calls is
always the table logical name; raw-fetch URLs carry entity-set (plural) names.
Production bundles have no source maps — always ask for the original source
repo first; the minified bundle + manifest is the documented maximum
recoverable without it.

## Step 6 — Command bar, both generations

**A. Classic (RibbonDiffXml)** — three locations in `Customizations.xml`:
1. Per-entity: `Entities/Entity/RibbonDiffXml` (free with the step-1 export).
2. **Application ribbon** (global / all-table buttons, `{!EntityLogicalName}`
   token): the ROOT `ImportExportXml/RibbonDiffXml` node — exported **only if**
   the *Application Ribbons* client-extension component was added to the
   solution before export. Easy to miss; check explicitly.
3. Per-form (legacy, not supported on Unified Interface — usually ignorable).

Mapping chain: `<CustomAction Id Location>` (Id encodes the bar:
`Mscrm.Form.<entity>.*` form · `Mscrm.HomepageGrid.<entity>.*` main grid ·
`Mscrm.SubGrid.<entity>.*` subgrid) → button's `Command` →
`<CommandDefinition>` → `<Actions>`: `JavaScriptFunction
Library="$webresource:<name>" FunctionName="<fn>"` or `Url Address="..."` →
`<EnableRules>/<DisplayRules>` (ValueRule, SelectionCountRule, FormStateRule,
RecordPrivilegeRule, CustomRule-calling-JS with 10s timeout…).
`<HideCustomAction>` removes built-in buttons. On the modern command bar,
commands failing EnableRules are **hidden, not greyed**.

Effective merged ribbon (runtime state):
```
GET /api/data/v9.2/RetrieveEntityRibbon(EntityName='<table>',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')
GET /api/data/v9.2/RetrieveApplicationRibbon
```
The response binary is Base64 → **a zip** → extract the `RibbonXml.xml` part
(any zip reader). Diff against Microsoft's published defaults
(`ExportedRibbonXml.zip` — downloadable sample linked from the "Export ribbon
definitions" page on Microsoft Learn) to isolate customizations.

**B. Modern (`appaction` rows):**
```
GET /api/data/v9.2/appactions?$select=name,uniquename,buttonlabeltext,location,context,contextvalue,sequence,type,hidden,isdisabled,origin,onclickeventtype,onclickeventformulacomponentlibrary,onclickeventformulacomponentname,onclickeventformulafunctionname,onclickeventjavascriptfunctionname,onclickeventjavascriptparameters,visibilitytype,visibilityformulafunctionname,statecode,ismanaged&$expand=AppModuleId($select=uniquename),ContextEntity($select=logicalname),OnClickEventJavaScriptWebResourceId($select=name),ParentAppActionId($select=uniquename)
```
- `location`: 0 Form · 1 Main Grid · 2 Sub Grid · 3 Associated Grid ·
  4 Quick Form · 5 Global Header · 6 Dashboard.
- `onclickeventtype`: **1 = Power Fx**, **2 = JavaScript** (one web resource +
  one function only — unlike classic, no action chaining).
- **Power Fx expressions are NOT in the row** — only name pointers. The logic
  lives in the app's **command component library** (a canvas app). Get its
  `.msapp` (`pac canvas download`, or `canvasapps/` in the YAML clone); it is a
  zip whose source is `Src\*.pa.yaml` — `pac canvas unpack --msapp <file>
  --sources <dir>` (preview) extracts it.
- `origin` records classic→modern migration; migrated classic visibility rules
  hang off the `appactionrule` M:M relation.
- `_appmoduleid_value` null appears to mean not app-scoped (table-wide/global
  command) — inferred from the column being optional plus the documented
  scoping model; confirm against a known command in the org.

**Deliverable for the fragment:** one table — Bar (entity + location) | Button
label | Generation | What runs (`file.function` / `component.function` / URL) |
When shown (rules summary) | Why (business purpose).

## Step 7 — Security model

```
GET /api/data/v9.2/roles?$select=name
GET /api/data/v9.2/roles(<roleid>)/roleprivileges_association?$select=name&$orderby=name
GET /api/data/v9.2/businessunits?$select=name,_parentbusinessunitid_value
GET /api/data/v9.2/fieldsecurityprofiles?$select=name&$expand=lk_fieldpermission_fieldsecurityprofileid($select=entityname,attributelogicalname,cancreate,canread,canupdate)
GET /api/data/v9.0/appmodules(<appmoduleid>)?$select=name&$expand=appmoduleroles_association
```
Watch out: `roleprivileges_association` returns privilege **names only**
(`prvReadAccount`…) — depth (User/BU/Parent-child/Org) needs the
`RetrieveRolePrivilegesRole` function. Privileges granted via teams need
`RetrieveUsersPrivilegesThroughTeams` if documenting *effective* access rather
than role definitions. Custom roles ≈ business personas — seed the user
stories from them.

## Step 7b — Embedded Power BI detection

Dashboards are `systemform` rows with `type eq 0`:

```
GET /api/data/v9.2/systemforms?$select=name,objecttypecode,formxml&$filter=type eq 0 and formactivationstate eq 1
```

Scan each dashboard's `formxml` for Power BI markers (e.g. `PowerBI`) to
enumerate embedded reports deterministically — the marker scan is a practical
heuristic, so confirm hits in the app. For each embedded report record: report
name | workspace/dataset | Dataverse tables feeding it (the `pbi-cli`
`power-bi-docs` skill can document the semantic model itself).

## Step 8 — Runtime verification tools

| Tool | How | What it catches |
|---|---|---|
| Monitor → FormEvents | maker portal / app Monitor session | JS handlers registered in code (`addOnLoad`/`addOnChange`) that FormXml never lists |
| Command Checker | append `&ribbondebug=true` to the app URL → button on each command bar | every command incl. hidden (italics), each rule's True/False/Skipped evaluation, solution layers — the authority on the merged classic+modern bar |
| Form-handler kill switches | `&flags=DisableFormHandlers=true` / `DisableFormLibraries` on the app URL | isolating whether a behavior is JS-driven |
| Audit history | record form → Related → Audit History | which automation actually fired on a test record |
