## ?? AI Self-Learning System API Endpoints

### Trade Predictions
- **POST** `/api/ai/predict-trades` - Generate AI trade predictions
- **POST** `/api/ai-alpha/prediction/{symbol}` - Single stock prediction
- **GET** `/api/ai-alpha/predictions/top` - Top ranked predictions

### Model Training & Management
- **POST** `/api/ai/retrain` - Manually trigger model retraining
- **GET** `/api/ai/model-version` - Get active model version info
- **GET** `/api/ai/model-versions` - Get all model version history
- **POST** `/api/ai/model-version/rollback` - Rollback to previous model

### Performance Monitoring
- **GET** `/api/ai/performance` - Get current model performance metrics
- **GET** `/api/ai/performance/monitor` - Monitor and auto-retrain if needed
- **GET** `/api/ai/dashboard` - Comprehensive AI system dashboard

### Trade Outcome Tracking
- **POST** `/api/ai/trade/record-entry` - Record new trade entry
- **POST** `/api/ai/trade/record-exit` - Record trade exit and outcome
- **GET** `/api/ai/trade-outcomes` - Get trade outcome history

### Market Analysis
- **GET** `/api/ai/market-regime` - Get current market regime
- **GET** `/api/ai-alpha/regime` - Detailed regime analysis

### Reinforcement Learning
- **POST** `/api/ai/optimize-factors` - Optimize meta-factor weights
- **GET** `/api/ai/factor-performance` - Get factor performance history

### Portfolio Optimization
- **POST** `/api/ai-alpha/portfolio/optimize` - AI-based portfolio optimization