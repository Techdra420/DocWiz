---
id: daily-operations
title: Daily Operations
discipline: operations
audiences:
- user
- support
- admin
---

# Daily Operations

## User screens {#user-screens}

The home screen shows the station state, the current task, and the last 5 movements.

![Receiving screen](../../assets/receiving-screen-placeholder.png)

* **Green banner** — station ready, no open task.
* **Yellow banner** — waiting for user action (confirm quantity, choose shelf).
* **Red banner** — error; the message line shows what and where.

## Daily startup {#daily-startup}

:::: steps

1. Switch the scanner station on; wait for the sign-in screen (about 30 s).
2. Sign in with your badge — the banner turns green.
3. ::: audience admin
   On the first start of the day, check that the overnight sync finished (Admin console → Sync log).
   :::
4. Open the task list and start the first task.

::::

::: audience support
If the sign-in screen does not appear within 2 minutes, check the station's network
status. A red network icon usually means the station lost its address; restart the
station (see [Common problems](dw:troubleshooting#common-problems)).
:::
