# Walkthroughs: each cloud, and the videos

Moved out of the [README](../README.md) so its first screen stays short. Every screenshot and video here is the built-in demo or the real app against development clouds; nothing is sent anywhere by looking.

## What it looks like, on each cloud

Same app, three clouds. Each cloud shows what it can and cannot do, honestly: the chips under each cloud's heading say whether ServiceHub can count, browse, confirm a fix and watch automatically there.

| | Azure Service Bus | AWS SQS / SNS | Google Pub/Sub |
|---|---|---|---|
| **Home** | ![Azure Home](screenshots/readme/home-azure.png) | ![AWS Home](screenshots/readme/home-aws.png) | ![Google Cloud Home](screenshots/readme/home-gcp.png) |
| **Dead letters** | ![Azure dead letters](screenshots/readme/dead-letters-azure.png) | ![AWS dead letters](screenshots/readme/dead-letters-aws.png) | ![Google Cloud dead letters](screenshots/readme/dead-letters-gcp.png) |
| **Step-by-step guide** | [Azure guide](clouds/azure.md) | [AWS guide](clouds/aws.md) | [Google Cloud guide](clouds/gcp.md) |


## Watch it work

One captioned video per cloud, about five minutes each: Home, Dead letters, Look now, replaying one, some or all, Active messages, Replayed, Auto Replay, approvals,
Connections, Settings, Help, then Advanced and back. No sound needed. Click a preview to play the full video.

| Azure Service Bus | AWS SQS / SNS | Google Pub/Sub |
|:---:|:---:|:---:|
| [![Azure Service Bus walkthrough — click to play](media/azure-preview.gif)](media/azure.mp4) | [![AWS SQS / SNS walkthrough — click to play](media/aws-preview.gif)](media/aws.mp4) | [![Google Pub/Sub walkthrough — click to play](media/gcp-preview.gif)](media/gcp.mp4) |
| [▶ Azure, 5:01](media/azure.mp4) | [▶ AWS, 5:03](media/aws.mp4) | [▶ Google, 5:35](media/gcp.mp4) |

<details><summary><b>Chapters</b> (Azure; AWS and Google are within a few seconds of these)</summary>

`0:07` Connect a cloud · `0:15` Home: every cloud · `0:26` Home: one cloud · `0:43` Dead letters and Look now · `0:57` Open a message · `1:05` Replay one ·
`1:29` Replay selected · `2:09` Replay All, then stop · `2:43` Active messages and Send · `2:57` Replayed · `3:07` Auto Replay · `3:25` Approve or decline ·
`3:37` Connections · `3:45` Settings · `4:03` Help · `4:09` Switch to Advanced · `5:02` Back to Simple

</details>

*Recorded from the real app against real development clouds. Replay All is started and then stopped on camera: **Stop now** leaves every message not yet sent exactly as it was.*

