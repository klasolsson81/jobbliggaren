FROM redis:8.6-alpine

RUN apk add --no-cache python3

ENV PYTHONDONTWRITEBYTECODE=1
ENTRYPOINT ["python3", "/repo/tests/Deployment/RedisPolicyVerification.test.py"]
