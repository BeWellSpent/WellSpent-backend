from typing import Union, Annotated

from fastapi import FastAPI, Depends, Body

from sqlmodel import Session
from src.db.session import get_session

from src.service.user_service import UserService
from src.service.budget_service import BudgetService

app = FastAPI()

from pydantic import BaseModel, Field

class BudgetCreateRequest(BaseModel):
    user_id : int | None = Field(default=None, title="user identifier")
    name : str | None = Field(default=None, title="budget name")

@app.get("/")
async def read_root(session: Session = Depends(get_session)):
    return UserService().get_all(session)


@app.get("/items/{item_id}")
async def read_item(item_id: int, q: Union[str, None] = None, session: Session = Depends(get_session)):
    return {"item_id": item_id, "q": q}

@app.put("/budget/create")
async def create_budget(budget_data: Annotated[BudgetCreateRequest, Body(embed=True)], session: Session = Depends(get_session)):
    return BudgetService().create_budget(session, budget_data)