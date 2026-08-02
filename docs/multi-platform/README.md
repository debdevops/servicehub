# Multi-Platform Guides

This folder keeps Azure, AWS, and GCP guidance in separate documents so each provider can be read on its own.

Choose the guide for the cloud you are working with:

- [Azure Guide](./azure/README.md)
- [AWS Guide](./aws/README.md)
- [GCP Guide](./gcp/README.md)

Each provider guide includes:

- beginner-friendly infrastructure setup
- portal or console steps
- CLI or automation-oriented steps
- testing and verification steps
- how to connect that provider to ServiceHub
- safety and least-privilege advice

Recommended reading order for a novice:

1. Azure first, because it is the clearest end-to-end ServiceHub path.
2. AWS second, to learn queue, DLQ, and SNS fanout.
3. GCP third, to learn Pub/Sub topics, subscriptions, and service account access.