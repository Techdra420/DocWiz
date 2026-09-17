---
id: installation
title: Installation & Network
discipline: setup
audiences:
- user
- support
- admin
---

# Installation & Network

## System requirements {#requirements}

Application server with 4 CPU cores, 8 GB RAM, and 50 GB free disk. The full
requirements sheet and the release notes are attached:
[Release notes (PDF)](../../attachments/release-notes-placeholder.pdf).

## Network and addresses {#network}

| Device | Address | Role |
| ------ | ------- | ---- |
| Application server | 10.0.0.10 | Web application and API |
| Database server | 10.0.0.11 | Stock ledger |
| Scanner station 1 | 10.0.0.21 | Receiving |
| Scanner station 2 | 10.0.0.22 | Picking |
| Label printer | 10.0.0.30 | Shelf and pallet labels |

::: audience admin
The firewall rules and certificate details are in the API reference → deployment. Keep
this table and the deployment in sync — this table is the handover truth.
:::
