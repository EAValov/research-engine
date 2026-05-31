CREATE SCHEMA IF NOT EXISTS nuq;

DO $$ BEGIN
  CREATE TYPE nuq.job_status AS ENUM ('queued', 'active', 'completed', 'failed');
EXCEPTION
  WHEN duplicate_object THEN null;
END $$;

CREATE TABLE IF NOT EXISTS nuq.queue_crawl_finished (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  status nuq.job_status NOT NULL DEFAULT 'queued'::nuq.job_status,
  data jsonb,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  priority int NOT NULL DEFAULT 0,
  lock uuid,
  locked_at timestamp with time zone,
  stalls integer,
  finished_at timestamp with time zone,
  listen_channel_id text,
  returnvalue jsonb,
  failedreason text,
  owner_id uuid,
  group_id uuid,
  CONSTRAINT queue_crawl_finished_pkey PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS queue_crawl_finished_active_locked_at_idx
  ON nuq.queue_crawl_finished USING btree (locked_at)
  WHERE (status = 'active'::nuq.job_status);

CREATE INDEX IF NOT EXISTS nuq_queue_crawl_finished_queued_optimal_2_idx
  ON nuq.queue_crawl_finished (priority ASC, created_at ASC, id)
  WHERE (status = 'queued'::nuq.job_status);

CREATE INDEX IF NOT EXISTS nuq_queue_crawl_finished_failed_created_at_idx
  ON nuq.queue_crawl_finished USING btree (created_at)
  WHERE (status = 'failed'::nuq.job_status);

CREATE INDEX IF NOT EXISTS nuq_queue_crawl_finished_completed_created_at_idx
  ON nuq.queue_crawl_finished USING btree (created_at)
  WHERE (status = 'completed'::nuq.job_status);
