# Notes authorization model

The sample has human users authenticated by SqlOS. Both the HTTP API and MCP tools
resolve the actor from a validated token; callers never supply a user ID.

Each user has one personal notebook, a root resource of type `notebook`. Notes
belong to that notebook and are checked against its permissions. There is no
organization hierarchy or administrative persona in this deliberately small sample.

| Permission | Resource | Operations |
| --- | --- | --- |
| `NOTES_READ` | The user's notebook | List notes through HTTP or MCP |
| `NOTES_WRITE` | The user's notebook | Add a note through HTTP or MCP |

The flat `notebook_owner` role contains both permissions. The notebook creation
transaction provisions the user's subject, notebook resource, and initial owner
grant once. Requests for an existing notebook only check access; they never repair
or recreate a removed grant. Re-enabling access is an explicit administrator action.

| Persona | Allowed | Denied |
| --- | --- | --- |
| Alice, owner of Alice's notebook | Read and add Alice's notes using API or MCP | Read Bob's notes |
| Bob, owner of Bob's notebook | Read and add Bob's notes using API or MCP | Read Alice's notes |
| Alice after owner-grant removal | Sign in | Read or add notes through either surface |
| Anonymous caller | Start hosted sign-in | API and MCP operations |

Creation is reserved by a unique notebook row in the same transaction as its resource and grant.
Tests must exercise concurrent first use, two-user isolation, removal of the grant,
and explicit restoration. Domain provisioning and checks live in `NotesService`,
shared by the API and MCP tools. Role/permission vocabulary is reconciled by the
existing SqlOS FGA seed service. Host topology is code configuration; stored grants
remain manageable through the existing FGA service, admin API, and dashboard.
