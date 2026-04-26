-- =============================================
-- Migration Rollback: Remove news ingestion phase 2 tables
-- Date: 2026-04-25
-- =============================================

DROP INDEX IF EXISTS idx_news_impacts_created_at;
DROP INDEX IF EXISTS idx_news_impacts_sector_id;
DROP INDEX IF EXISTS idx_news_impacts_instrument_id;
DROP INDEX IF EXISTS idx_news_impacts_article_id;

DROP INDEX IF EXISTS idx_news_keywords_keyword;
DROP INDEX IF EXISTS idx_news_keywords_article_id;

DROP INDEX IF EXISTS idx_news_articles_source;
DROP INDEX IF EXISTS idx_news_articles_published_at;
DROP INDEX IF EXISTS idx_news_articles_external_id;

DROP TABLE IF EXISTS news_impacts;
DROP TABLE IF EXISTS news_keywords;
DROP TABLE IF EXISTS news_articles;
