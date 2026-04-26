using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TradingSystem.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortfolioLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_model_versions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    model_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    training_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    training_dataset_size = table.Column<int>(type: "integer", nullable: false),
                    validation_dataset_size = table.Column<int>(type: "integer", nullable: false),
                    training_duration = table.Column<string>(type: "text", nullable: false),
                    hyperparameters_json = table.Column<string>(type: "jsonb", nullable: false),
                    feature_importance_json = table.Column<string>(type: "jsonb", nullable: false),
                    training_accuracy = table.Column<float>(type: "real", nullable: false),
                    validation_accuracy = table.Column<float>(type: "real", nullable: false),
                    win_rate = table.Column<float>(type: "real", nullable: false),
                    profit_factor = table.Column<float>(type: "real", nullable: false),
                    sharpe_ratio = table.Column<float>(type: "real", nullable: false),
                    max_drawdown = table.Column<float>(type: "real", nullable: false),
                    average_prediction_error = table.Column<float>(type: "real", nullable: false),
                    total_predictions = table.Column<int>(type: "integer", nullable: false),
                    successful_predictions = table.Column<int>(type: "integer", nullable: false),
                    production_accuracy = table.Column<float>(type: "real", nullable: false),
                    production_sharpe_ratio = table.Column<float>(type: "real", nullable: false),
                    total_pnl = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deprecation_reason = table.Column<string>(type: "text", nullable: true),
                    model_file_path = table.Column<string>(type: "text", nullable: false),
                    checkpoint_path = table.Column<string>(type: "text", nullable: false),
                    change_log = table.Column<string>(type: "text", nullable: false),
                    improvement_notes = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deprecated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_model_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "factor_performance_tracking",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    momentum_weight = table.Column<float>(type: "real", nullable: false),
                    trend_weight = table.Column<float>(type: "real", nullable: false),
                    volatility_weight = table.Column<float>(type: "real", nullable: false),
                    liquidity_weight = table.Column<float>(type: "real", nullable: false),
                    relative_strength_weight = table.Column<float>(type: "real", nullable: false),
                    sentiment_weight = table.Column<float>(type: "real", nullable: false),
                    risk_weight = table.Column<float>(type: "real", nullable: false),
                    momentum_win_rate = table.Column<float>(type: "real", nullable: false),
                    momentum_avg_return = table.Column<float>(type: "real", nullable: false),
                    momentum_trade_count = table.Column<int>(type: "integer", nullable: false),
                    trend_win_rate = table.Column<float>(type: "real", nullable: false),
                    trend_avg_return = table.Column<float>(type: "real", nullable: false),
                    trend_trade_count = table.Column<int>(type: "integer", nullable: false),
                    sentiment_win_rate = table.Column<float>(type: "real", nullable: false),
                    sentiment_avg_return = table.Column<float>(type: "real", nullable: false),
                    sentiment_trade_count = table.Column<int>(type: "integer", nullable: false),
                    total_trades = table.Column<float>(type: "real", nullable: false),
                    overall_win_rate = table.Column<float>(type: "real", nullable: false),
                    overall_sharpe_ratio = table.Column<float>(type: "real", nullable: false),
                    recommended_adjustments_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_factor_performance_tracking", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fusion_learning_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    iteration = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    technical_weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    news_weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    sector_weight = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    minimum_fusion_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    news_negative_boundary = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    news_positive_boundary = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    prior_performance_metrics_json = table.Column<string>(type: "jsonb", nullable: true),
                    prior_config_json = table.Column<string>(type: "jsonb", nullable: true),
                    reasoning_text = table.Column<string>(type: "text", nullable: true),
                    sessions_analyzed = table.Column<int>(type: "integer", nullable: false),
                    risk_assessment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rolled_back_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sessions_completed_under_this_config = table.Column<int>(type: "integer", nullable: false),
                    performance_under_this_config_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fusion_learning_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_sentiments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sentiment = table.Column<string>(type: "text", nullable: false),
                    sentiment_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    volatility_index = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    market_breadth = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    rsi = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    macd_histogram = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    price_vs_20dma = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    price_vs_50dma = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    new_highs_52w = table.Column<int>(type: "integer", nullable: false),
                    new_lows_52w = table.Column<int>(type: "integer", nullable: false),
                    mclellan_oscillator = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    vix_vs_20dma = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    index_performance = table.Column<string>(type: "jsonb", nullable: false),
                    sector_performance = table.Column<string>(type: "jsonb", nullable: false),
                    key_factors = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_sentiments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "news_articles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    headline = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ingested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sentiment = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sentiment_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_articles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_manager_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    session_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    initial_capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    risk_profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    preferred_sectors = table.Column<string>(type: "jsonb", nullable: false),
                    preferred_themes = table.Column<string>(type: "jsonb", nullable: false),
                    auto_rebalance_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    max_positions = table.Column<int>(type: "integer", nullable: false),
                    timeframe_minutes = table.Column<int>(type: "integer", nullable: false),
                    min_confidence = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    last_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_runs = table.Column<int>(type: "integer", nullable: false),
                    allocated_capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    realized_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    unrealized_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    win_rate_percent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_manager_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sectors",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sectors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    upstox_access_token = table.Column<string>(type: "text", nullable: true),
                    upstox_refresh_token = table.Column<string>(type: "text", nullable: true),
                    token_issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    preferred_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    preferred_risk_profile = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    preferred_sectors = table.Column<string>(type: "jsonb", nullable: false),
                    preferred_themes = table.Column<string>(type: "jsonb", nullable: false),
                    auto_rebalance_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_on = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "news_keywords",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    article_id = table.Column<long>(type: "bigint", nullable: false),
                    keyword = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    relevance_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_keywords", x => x.id);
                    table.ForeignKey(
                        name: "FK_news_keywords_news_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "news_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_performance_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    win_rate = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    sharpe_ratio = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    max_drawdown = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    profit_factor = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    average_hold_days = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    average_hold_efficiency = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    average_fusion_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    veto_rejection_rate = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    total_trades = table.Column<int>(type: "integer", nullable: false),
                    winning_trades = table.Column<int>(type: "integer", nullable: false),
                    losing_trades = table.Column<int>(type: "integer", nullable: false),
                    total_pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    active_fusion_learning_config_iteration = table.Column<int>(type: "integer", nullable: true),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_performance_histories", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_performance_histories_portfolio_manager_sessions_~",
                        column: x => x.session_id,
                        principalTable: "portfolio_manager_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    exchange = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sector_id = table.Column<int>(type: "integer", nullable: true),
                    industry = table.Column<string>(type: "text", nullable: false),
                    market_cap = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    isin = table.Column<string>(type: "text", nullable: false),
                    instrument_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    lot_size = table.Column<int>(type: "integer", nullable: false),
                    tick_size = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_derivatives_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    default_trading_mode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.id);
                    table.ForeignKey(
                        name: "FK_instruments_sectors_sector_id",
                        column: x => x.sector_id,
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "feature_store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    features_json = table.Column<string>(type: "jsonb", nullable: false),
                    feature_count = table.Column<int>(type: "integer", nullable: false),
                    feature_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feature_store", x => x.id);
                    table.ForeignKey(
                        name: "FK_feature_store_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "indicator_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    timeframe_minutes = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ema_fast = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    ema_slow = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    rsi = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    macd_line = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    macd_signal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    macd_histogram = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    adx = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    plus_di = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    minus_di = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    atr = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    bollinger_upper = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    bollinger_middle = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    bollinger_lower = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    vwap = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_indicator_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_indicator_snapshots_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "instrument_prices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    timeframe = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_prices", x => x.id);
                    table.ForeignKey(
                        name: "FK_instrument_prices_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_candles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    timeframe_minutes = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    open = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    high = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    low = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_candles", x => new { x.id, x.timestamp, x.timeframe_minutes });
                    table.ForeignKey(
                        name: "FK_market_candles_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "news_impacts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    article_id = table.Column<long>(type: "bigint", nullable: false),
                    instrument_id = table.Column<int>(type: "integer", nullable: true),
                    sector_id = table.Column<int>(type: "integer", nullable: true),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    impact_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_impacts", x => x.id);
                    table.ForeignKey(
                        name: "FK_news_impacts_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_news_impacts_news_articles_article_id",
                        column: x => x.article_id,
                        principalTable: "news_articles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_news_impacts_sectors_sector_id",
                        column: x => x.sector_id,
                        principalTable: "sectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "portfolio_manager_trades",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    instrument_name = table.Column<string>(type: "text", nullable: false),
                    sector = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    current_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    allocated_capital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    allocation_percent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fusion_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    fusion_news_signal = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    fusion_technical_signal = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    fusion_sector_signal = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    fusion_direction_veto = table.Column<bool>(type: "boolean", nullable: true),
                    fusion_included = table.Column<bool>(type: "boolean", nullable: true),
                    fusion_evidence = table.Column<string>(type: "text", nullable: true),
                    pnl = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pnl_percent = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    entry_reasoning = table.Column<string>(type: "text", nullable: false),
                    exit_reasoning = table.Column<string>(type: "text", nullable: true),
                    signals = table.Column<string>(type: "jsonb", nullable: false),
                    model_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portfolio_manager_trades", x => x.id);
                    table.ForeignKey(
                        name: "FK_portfolio_manager_trades_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_portfolio_manager_trades_portfolio_manager_sessions_session~",
                        column: x => x.session_id,
                        principalTable: "portfolio_manager_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recommendations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    entry_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    risk_reward_ratio = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    option_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    option_strike = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    explanation_text = table.Column<string>(type: "text", nullable: false),
                    reasoning_points = table.Column<string>(type: "jsonb", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recommendations", x => x.id);
                    table.ForeignKey(
                        name: "FK_recommendations_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    market_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    setup_score = table.Column<int>(type: "integer", nullable: false),
                    bias = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    adx_score = table.Column<int>(type: "integer", nullable: false),
                    rsi_score = table.Column<int>(type: "integer", nullable: false),
                    ema_vwap_score = table.Column<int>(type: "integer", nullable: false),
                    volume_score = table.Column<int>(type: "integer", nullable: false),
                    bollinger_score = table.Column<int>(type: "integer", nullable: false),
                    structure_score = table.Column<int>(type: "integer", nullable: false),
                    last_close = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    atr = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_scan_snapshots_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trade_outcomes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    symbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entry_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exit_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entry_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    predicted_return = table.Column<float>(type: "real", nullable: false),
                    predicted_success_probability = table.Column<float>(type: "real", nullable: false),
                    predicted_risk_score = table.Column<float>(type: "real", nullable: false),
                    model_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    meta_factors_json = table.Column<string>(type: "jsonb", nullable: false),
                    market_regime_at_entry = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    regime_confidence = table.Column<float>(type: "real", nullable: false),
                    actual_return = table.Column<decimal>(type: "numeric", nullable: true),
                    profit_loss = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    profit_loss_percent = table.Column<decimal>(type: "numeric", nullable: true),
                    is_successful = table.Column<bool>(type: "boolean", nullable: true),
                    prediction_error = table.Column<float>(type: "real", nullable: true),
                    prediction_accuracy_score = table.Column<float>(type: "real", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    learning_tags = table.Column<string>(type: "jsonb", nullable: false),
                    strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sector = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_outcomes", x => x.id);
                    table.ForeignKey(
                        name: "FK_trade_outcomes_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trades",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instrument_id = table.Column<int>(type: "integer", nullable: false),
                    trade_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entry_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    exit_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    entry_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    exit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    stop_loss = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    atr_at_entry = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    option_symbol = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    option_strike = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    option_entry_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    option_exit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    entry_reason = table.Column<string>(type: "text", nullable: false),
                    exit_reason = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pnl = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pnl_percent = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EntryIndicatorsJson = table.Column<string>(type: "text", nullable: true),
                    ExitIndicatorsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trades", x => x.id);
                    table.ForeignKey(
                        name: "FK_trades_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_versions_active",
                table: "ai_model_versions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_versions_status",
                table: "ai_model_versions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_ai_model_versions_unique",
                table: "ai_model_versions",
                columns: new[] { "version", "model_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_factor_performance_period",
                table: "factor_performance_tracking",
                columns: new[] { "period_start", "period_end" });

            migrationBuilder.CreateIndex(
                name: "idx_feature_store_instrument_time",
                table: "feature_store",
                columns: new[] { "instrument_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_feature_store_symbol",
                table: "feature_store",
                column: "symbol");

            migrationBuilder.CreateIndex(
                name: "idx_feature_store_timestamp",
                table: "feature_store",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_feature_store_version",
                table: "feature_store",
                column: "feature_version");

            migrationBuilder.CreateIndex(
                name: "idx_fusion_learning_config_applied_at",
                table: "fusion_learning_configs",
                column: "applied_at");

            migrationBuilder.CreateIndex(
                name: "idx_fusion_learning_config_created_at",
                table: "fusion_learning_configs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_fusion_learning_config_iteration",
                table: "fusion_learning_configs",
                column: "iteration",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_fusion_learning_config_status",
                table: "fusion_learning_configs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_indicator_snapshots_instrument",
                table: "indicator_snapshots",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_indicator_snapshots_lookup",
                table: "indicator_snapshots",
                columns: new[] { "instrument_id", "timeframe_minutes", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_instrument_prices_instrument_timeframe",
                table: "instrument_prices",
                columns: new[] { "instrument_id", "timeframe" });

            migrationBuilder.CreateIndex(
                name: "idx_instrument_prices_timestamp",
                table: "instrument_prices",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_instrument_prices_unique",
                table: "instrument_prices",
                columns: new[] { "instrument_id", "timeframe", "timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_instruments_active",
                table: "instruments",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "idx_instruments_key",
                table: "instruments",
                column: "instrument_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_instruments_sector_id",
                table: "instruments",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "idx_instruments_symbol",
                table: "instruments",
                column: "symbol");

            migrationBuilder.CreateIndex(
                name: "idx_market_candles_chart_query",
                table: "market_candles",
                columns: new[] { "instrument_id", "timeframe_minutes", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_market_candles_instrument",
                table: "market_candles",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_market_sentiments_created_at",
                table: "market_sentiments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_market_sentiments_sentiment",
                table: "market_sentiments",
                column: "sentiment");

            migrationBuilder.CreateIndex(
                name: "idx_market_sentiments_timestamp",
                table: "market_sentiments",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "idx_news_articles_external_id",
                table: "news_articles",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_news_articles_published_at",
                table: "news_articles",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "idx_news_articles_source",
                table: "news_articles",
                column: "source");

            migrationBuilder.CreateIndex(
                name: "idx_news_impacts_article_id",
                table: "news_impacts",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "idx_news_impacts_created_at",
                table: "news_impacts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_news_impacts_instrument_id",
                table: "news_impacts",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_news_impacts_sector_id",
                table: "news_impacts",
                column: "sector_id");

            migrationBuilder.CreateIndex(
                name: "idx_news_keywords_article_id",
                table: "news_keywords",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "idx_news_keywords_keyword",
                table: "news_keywords",
                column: "keyword");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_sessions_updated_at",
                table: "portfolio_manager_sessions",
                column: "updated_at");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_sessions_user_id",
                table: "portfolio_manager_sessions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_sessions_user_status",
                table: "portfolio_manager_sessions",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_trades_created_at",
                table: "portfolio_manager_trades",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_trades_instrument",
                table: "portfolio_manager_trades",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_manager_trades_session_status",
                table: "portfolio_manager_trades",
                columns: new[] { "session_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_perf_history_recorded_at",
                table: "portfolio_performance_histories",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "idx_portfolio_perf_history_session_id",
                table: "portfolio_performance_histories",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_recommendations_active",
                table: "recommendations",
                columns: new[] { "is_active", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_recommendations_confidence",
                table: "recommendations",
                columns: new[] { "confidence", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_recommendations_instrument",
                table: "recommendations",
                columns: new[] { "instrument_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_scan_snapshots_instrument",
                table: "scan_snapshots",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_scan_snapshots_lookup",
                table: "scan_snapshots",
                columns: new[] { "instrument_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_scan_snapshots_score",
                table: "scan_snapshots",
                columns: new[] { "setup_score", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "idx_sectors_code",
                table: "sectors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_sectors_name",
                table: "sectors",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "idx_trade_outcomes_entry_time",
                table: "trade_outcomes",
                column: "entry_time");

            migrationBuilder.CreateIndex(
                name: "idx_trade_outcomes_instrument",
                table: "trade_outcomes",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_trade_outcomes_model_version",
                table: "trade_outcomes",
                column: "model_version");

            migrationBuilder.CreateIndex(
                name: "idx_trade_outcomes_regime_success",
                table: "trade_outcomes",
                columns: new[] { "market_regime_at_entry", "is_successful" });

            migrationBuilder.CreateIndex(
                name: "idx_trade_outcomes_status",
                table: "trade_outcomes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_trades_entry_time",
                table: "trades",
                column: "entry_time");

            migrationBuilder.CreateIndex(
                name: "idx_trades_instrument",
                table: "trades",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "idx_trades_state",
                table: "trades",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "idx_user_profiles_updated_on",
                table: "user_profiles",
                column: "updated_on");

            migrationBuilder.CreateIndex(
                name: "idx_user_profiles_user_id",
                table: "user_profiles",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_model_versions");

            migrationBuilder.DropTable(
                name: "factor_performance_tracking");

            migrationBuilder.DropTable(
                name: "feature_store");

            migrationBuilder.DropTable(
                name: "fusion_learning_configs");

            migrationBuilder.DropTable(
                name: "indicator_snapshots");

            migrationBuilder.DropTable(
                name: "instrument_prices");

            migrationBuilder.DropTable(
                name: "market_candles");

            migrationBuilder.DropTable(
                name: "market_sentiments");

            migrationBuilder.DropTable(
                name: "news_impacts");

            migrationBuilder.DropTable(
                name: "news_keywords");

            migrationBuilder.DropTable(
                name: "portfolio_manager_trades");

            migrationBuilder.DropTable(
                name: "portfolio_performance_histories");

            migrationBuilder.DropTable(
                name: "recommendations");

            migrationBuilder.DropTable(
                name: "scan_snapshots");

            migrationBuilder.DropTable(
                name: "trade_outcomes");

            migrationBuilder.DropTable(
                name: "trades");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "news_articles");

            migrationBuilder.DropTable(
                name: "portfolio_manager_sessions");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "sectors");
        }
    }
}
