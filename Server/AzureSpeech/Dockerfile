FROM python:3.12-slim-bookworm
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 SPEECH_DATA_DIR=/data
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends libasound2 libssl3 ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY requirements.lock.txt .
RUN pip install --no-cache-dir -r requirements.lock.txt \
    && useradd --uid 10001 --create-home speech \
    && mkdir /data && chown speech:speech /data
COPY app.py continuous_stt.py check_connection.py ./
USER speech
EXPOSE 8091
CMD ["python", "-m", "uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8091", "--workers", "1", "--no-access-log"]
