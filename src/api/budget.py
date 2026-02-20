from fastapi import APIRouter, Depends, Body
from fastapi import HTTPException
from typing import Annotated
from sqlmodel import Session
from db.session import get_session
from service.budget_service import BudgetService
from schemas.budget import BudgetCreateRequest, AddPeopleToBudgetRequest, Budget
from exceptions.budget import BudgetException
import logging as log

router = APIRouter()

@router.get("/budgets", response_model=list[Budget])
async def get_budgets(session: Session = Depends(get_session)):
    log.info("[get_budgets] Retrieving all budgets")
    return BudgetService().get_all(session)

@router.get("/budget/{budget_id}", response_model=Budget)
async def get_budget(budget_id: str, session: Session = Depends(get_session)):
    log.info("[get_budget] Retrieving budget with id: %s", budget_id)
    budget = BudgetService().get_budget_by_id(session, budget_id)
    if budget is None:
        raise BudgetException(f"Budget with id {budget_id} not found")
    log.info("[get_budget] Budget retrieved successfully: %s", budget)
    return budget

@router.put("/budget/create", response_model=Budget)
async def create_budget(budget_data: Annotated[BudgetCreateRequest, Body(embed=True)], session: Session = Depends(get_session)):
    log.info("[create_budget] Creating budget with data: %s", budget_data)
    budget = BudgetService().create_budget(session, budget_data)
    if budget is None:
        raise BudgetException(f"Failed to create budget")
    log.info("[create_budget] Budget created successfully: %s", budget)        
    return budget
    
@router.put("/budget/add-people", response_model=Budget)
async def add_people(people_data: Annotated[AddPeopleToBudgetRequest, Body(embed=True)], session: Session = Depends(get_session)):
    log.info("[add_people] Adding people to budget with id: %s", people_data)
    if people_data.budget_id is None or people_data.budget_id.strip() == "":
        raise BudgetException(f"Budget ID is required")
    
    response = BudgetService().add_people_to_budget(session, people_data.budget_id, people=people_data.people)
    return response
