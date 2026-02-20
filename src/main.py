from fastapi import FastAPI
import logging as log

from fastapi.responses import JSONResponse
from core.logging_config import setup_logging
from exceptions.budget import BudgetException

setup_logging()

app = FastAPI()
log.getLogger('api').setLevel(log.CRITICAL)
log.getLogger("watchfiles.main").setLevel(log.WARNING)
# Import and include routers
from api.user import router as user_router
from api.budget import router as budget_router

app.include_router(user_router)
app.include_router(budget_router)

@app.exception_handler(BudgetException)
async def budget_exception_handler(request, exc: BudgetException):
    return JSONResponse(
        status_code=400,
        content={"success": False, "error": str(exc)}
    )
