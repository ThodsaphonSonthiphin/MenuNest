# Azure DevOps — fetching a work item with attachments

Worked commands (PowerShell + az CLI, Entra auth). Replace `Cartagena365`/`GlassHull` with the target org/project — derive them from the repo's `git remote -v` (dev.azure.com/{org}/{project}/_git/...) or ask the user.

## 1. Token

```powershell
$token = az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv
```

`499b84ac-1321-427f-aa17-267ca6975798` is the fixed Azure DevOps resource ID — it works even when the account has no Azure subscription. If this fails: `az login` first (use the work account, not personal). PAT alternative: header `Authorization: Basic <base64(":"+PAT)>`.

## 2. Work item

```powershell
$r = Invoke-RestMethod -Uri "https://dev.azure.com/{org}/{project}/_apis/wit/workitems/{id}?api-version=7.0" -Headers @{Authorization="Bearer $token"}
$r.fields.'System.WorkItemType'; $r.fields.'System.Title'; $r.fields.'System.State'
$r.fields.'System.Description'                          # HTML — may be ONLY an <img> tag
$r.fields.'Microsoft.VSTS.TCM.ReproSteps'               # Bugs keep their text here, not in Description
$r.fields.'Microsoft.VSTS.Common.AcceptanceCriteria'
```

Ticket URL for the user: `https://dev.azure.com/{org}/{project}/_workitems/edit/{id}`

## 3. Comments (decisions often live here)

```powershell
(Invoke-RestMethod -Uri "https://dev.azure.com/{org}/{project}/_apis/wit/workItems/{id}/comments?api-version=7.0-preview.3" -Headers @{Authorization="Bearer $token"}).comments | ForEach-Object { $_.createdBy.displayName + ': ' + $_.text }
```

## 4. Attached images — download and READ them

The Description HTML embeds attachments as
`<img src="https://dev.azure.com/{org}/{projectGuid}/_apis/wit/attachments/{guid}?fileName=image.png">`.
Extract the URL, download with the same Bearer token, then view the file with the Read tool:

```powershell
Invoke-WebRequest -Uri "<attachment url>" -Headers @{Authorization="Bearer $token"} -OutFile "$env:TEMP\ticket{id}.png"
```

An annotated screenshot is frequently the entire requirement ("Rename Auto to Vehicles / Hide Breakbulk" existed only as red boxes on a screenshot in ticket #5887). Never conclude "description is empty" without opening the images.

## Gotchas

- 401/403 → token expired or wrong tenant: re-run `az login` with the work account.
- 404 on a work item → wrong org/project for that number, or the number wasn't a ticket id at all.
- The `projectGuid` inside attachment URLs differs from the project name — use the URL exactly as found in the HTML.
- `System.Description` empty on Bugs → check `Microsoft.VSTS.TCM.ReproSteps`.
